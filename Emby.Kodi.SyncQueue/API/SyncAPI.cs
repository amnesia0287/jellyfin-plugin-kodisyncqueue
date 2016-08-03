﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Common.Configuration;
using Emby.Kodi.SyncQueue.Entities;
using Emby.Kodi.SyncQueue.Data;

namespace Emby.Kodi.SyncQueue.API
{
    public class SyncAPI : IRestfulService
    {
        private readonly ILogger _logger;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IApplicationPaths _applicationPaths;

        //private DataHelper dataHelper;

        public SyncAPI(ILogger logger, IJsonSerializer jsonSerializer, IApplicationPaths applicationPaths)
        {
            _logger = logger;
            _jsonSerializer = jsonSerializer;
            _applicationPaths = applicationPaths;

            _logger.Debug("Emby.Kodi.SyncQueue:  SyncAPI Created and Listening at \"/Emby.Kodi.SyncQueue/{UserID}/{LastUpdateDT}/GetItems?format=json\" - {LastUpdateDT} must be a UTC DateTime formatted as yyyy-MM-ddTHH:mm:ssZ");
            _logger.Debug("Emby.Kodi.SyncQueue:  SyncAPI Created and Listening at \"/Emby.Kodi.SyncQueue/{UserID}/GetItems?LastUpdateDT={LastUpdateDT}&format=json\" - {LastUpdateDT} must be a UTC DateTime formatted as yyyy-MM-ddTHH:mm:ssZ");
        }

        public SyncUpdateInfo Get(GetLibraryItems request)
        {
            _logger.Info(String.Format("Emby.Kodi.SyncQueue:  Sync Requested for UserID: '{0}' with LastUpdateDT: '{1}'", request.UserID, request.LastUpdateDT));
            _logger.Debug("Emby.Kodi.SyncQueue:  Processing message...");
            var info = new SyncUpdateInfo();
            if (request.LastUpdateDT == null || request.LastUpdateDT == "")
                request.LastUpdateDT = "1900-01-01T00:00:00Z";

            Task<SyncUpdateInfo> x = PopulateLibraryInfo(request.UserID, request.LastUpdateDT);
            Task.WhenAll(x);
            
            _logger.Debug("Emby.Kodi.SyncQueue:  Request processed... Returning result...");
            return x.Result;
        }

        public SyncUpdateInfo Get(GetLibraryItemsQuery request)
        {
            _logger.Info(String.Format("Emby.Kodi.SyncQueue:  Sync Requested for UserID: '{0}' with LastUpdateDT: '{1}'", request.UserID, request.LastUpdateDT));
            _logger.Debug("Emby.Kodi.SyncQueue:  Processing message...");
            if (request.LastUpdateDT == null || request.LastUpdateDT == "")
                request.LastUpdateDT = "1900-01-01T00:00:00Z";

            Task<SyncUpdateInfo> x = PopulateLibraryInfo(request.UserID, request.LastUpdateDT);
            Task.WhenAll(x);

            _logger.Debug("Emby.Kodi.SyncQueue:  Request processed... Returning result...");
            return x.Result;
        }

        public async Task<SyncUpdateInfo> PopulateLibraryInfo(string userId, string lastDT)
        {
            var startTime = DateTime.UtcNow;

            _logger.Debug("Emby.Kodi.SyncQueue:  Starting PopulateLibraryInfo...");
            var userDataChangedJson = new List<string>();
            var tmpList = new List<string>();

            var info = new SyncUpdateInfo();

            var userDT = Convert.ToDateTime(lastDT);
            var dtl = (long)(userDT.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds);

            _logger.Debug("Emby.Kodi.SyncQueue:  PopulateLibraryInfo:  Getting Items Added Info...");
            Task<List<string>> t1 = Task.Run(() =>
            {
                var result = DbRepo.GetItems(dtl, 0, userId);
                

                if (result.Count > 0)
                {
                    _logger.Info(String.Format("Emby.Kodi.SyncQueue:  Added Items Found: {0}", string.Join(",", result.ToArray())));
                }
                else
                {
                    _logger.Info("Emby.Kodi.SyncQueue:  No Added Items Found!");
                }
                return result;
            });

            _logger.Debug("Emby.Kodi.SyncQueue:  PopulateLibraryInfo:  Getting Items Removed Info...");
            Task<List<string>> t2 = Task.Run(() =>
            {
                var result = DbRepo.GetItems(dtl, 2, userId);

                if (result.Count > 0)
                {
                    _logger.Info(String.Format("Emby.Kodi.SyncQueue:  Removed Items Found: {0}", string.Join(",", result.ToArray())));
                }
                else
                {
                    _logger.Info("Emby.Kodi.SyncQueue:  No Removed Items Found!");
                }
                return result;
            });

            _logger.Debug("Emby.Kodi.SyncQueue:  PopulateLibraryInfo:  Getting Items Updated Info...");
            Task<List<string>> t3 = Task.Run(() =>
            {
                var result = DbRepo.GetItems(dtl, 1, userId);
                
                if (result.Count > 0)
                {
                    _logger.Info(String.Format("Emby.Kodi.SyncQueue:  Updated Items Found: {0}", string.Join(",", result.ToArray())));
                }
                else
                {
                    _logger.Info("Emby.Kodi.SyncQueue:  No Updated Items Found!");
                }
                return result;
            });

            _logger.Debug("Emby.Kodi.SyncQueue:  PopulateLibraryInfo:  Getting Folders Added To Info...");
            info.FoldersAddedTo.Clear();
            _logger.Debug("Emby.Kodi.SyncQueue:  PopulateLibraryInfo:  Getting Folders Removed From Info...");
            info.FoldersRemovedFrom.Clear();
            _logger.Debug("Emby.Kodi.SyncQueue:  PopulateLibraryInfo:  Getting User Data Changed Info...");
            Task<List<string>> t4 = Task.Run(() =>
            {
                List<string> ids;
                var result = DbRepo.GetUserInfos(dtl, userId, out ids);
                
                if (result.Count > 0)
                {
                    _logger.Info(String.Format("Emby.Kodi.SyncQueue:  User Data Changed Info Found: {0}", string.Join(",", ids.ToArray())));
                }
                else
                {
                    _logger.Info("Emby.Kodi.SyncQueue:  No User Data Changed Info Found!");
                }
                return result;
            });

            await Task.WhenAll(t1, t2, t3, t4);

            info.ItemsAdded = t1.Result;
            info.ItemsRemoved = t2.Result;
            info.ItemsUpdated = t3.Result;
            userDataChangedJson = t4.Result;

            foreach (var userData in userDataChangedJson)
            {
                info.UserDataChanged.Add(_jsonSerializer.DeserializeFromString<UserItemDataDto>(userData));
            }

            var json = _jsonSerializer.SerializeToString(info.UserDataChanged).ToString();
            _logger.Debug(json);
            TimeSpan diffDate = DateTime.UtcNow - startTime;
            _logger.Info(String.Format("Emby.Kodi.SyncQueue: Request Finished Taking {0}", diffDate.ToString("c")));

            return info;
        }
    }
}
