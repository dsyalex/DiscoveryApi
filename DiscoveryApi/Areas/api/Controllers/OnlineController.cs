﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using DiscoveryApi.Models;
using DiscoveryApi.DAL;
using Microsoft.Extensions.Logging;
using DiscoveryApi.Utils;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

// For more information on enabling MVC for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace DiscoveryApi.Controllers
{
    [Area("api"), Route("[area]/[controller]/[action]")]
    public class OnlineController : Controller
    {
        private readonly ILogger<OnlineController> logger;
        private readonly apiContext context;
        public OnlineController(apiContext _context, ILogger<OnlineController> _logger)
        {
            context = _context;
            logger = _logger;
        }

        // GET: /<controller>/
        [HttpGet("{key}")]
        public JsonResult GetPlayers(string key)
        {
            var model = new PlayersOnline<PlayerOnlineSingle>();
            if (!isValidKey(key))
            {
                logger.LogWarning("Illegal access attempt with key: " + key, ", ip: " + HttpContext.Request.Host);
                model.Error = Ressources.ApiResource.UnauthorizedAccess;
                return Json(model);
            }

            CacheManager cm = CacheManager.Instance;

            //Check if we have to renew the cache
            var now = DateTime.UtcNow;

            if (cm.LastPlayerOnlineCache.AddSeconds(cm.LastPlayerOnlineCacheDuration) < now)
            {
                model.Timestamp = cm.LastUpdate.ToString("yyyy-MM-ddTHH:mm:ss");
                model.Players = new List<PlayerOnlineSingle>();
                var players = context.ServerSessions.Include(c => c.ServerSessionsDataConn).Where(c => !c.SessionEnd.HasValue).ToList();
                var systems = context.Systems.ToList();
                var regions = context.Regions.ToList();
                //We can add the factions later

                foreach (var item in players)
                {
                    var player = new PlayerOnlineSingle();
                    player.Name = item.PlayerName;

                    TimeSpan span = now.Subtract(item.SessionStart);
                    if (span.Hours > 0)
                        player.Time = span.Hours.ToString() + "h" + (span.Minutes < 10 ? "0" : "") + span.Minutes.ToString();
                    else
                        player.Time = span.Minutes.ToString() + "m";

                    //Last location data
                    var last_system = item.ServerSessionsDataConn.OrderByDescending(c => c.Stamp).FirstOrDefault();
                    if (last_system != null)
                    {
                        var system = systems.SingleOrDefault(c => c.Nickname == last_system.Location.ToUpper()) ?? null;

                        //This is the always expected scenario
                        player.System = system != null ? system.Name : "Unknown";
                        player.Region = system != null ? regions.SingleOrDefault(c => c.Id == system.RegionId)?.Name ?? "Unknown" : "Unknown";
                        player.Ping = last_system.Ping;
                    }
                    else
                    {
                        //But better be safe than sorry
                        player.Ping = 0;
                        player.System = "ERROR";
                        player.Region = "ERROR";
                    }

                    model.Players.Add(player);
                }

                cm.PlayerOnlineCache = JsonConvert.SerializeObject(model);
                cm.LastPlayerOnlineCache = DateTime.UtcNow;
            }

            return Json(cm.PlayerOnlineCache);
        }

        [HttpGet("{key}/{page}")]
        public JsonResult GetAllPlayers(string key, string page)
        {
            var model = new GlobalDetailsModel();

            var pageInt = -1;
            if (!Int32.TryParse(page, out pageInt))
            {
                model.Error = Ressources.ApiResource.InvalidPageParameter;
                return Json(model);
            }

            if (!isValidKey(key))
            {
                logger.LogWarning("Illegal access attempt with key: " + key, ", ip: " + HttpContext.Request.Host);
                model.Error = Ressources.ApiResource.UnauthorizedAccess;
                return Json(model);
            }

            CacheManager cm = CacheManager.Instance;
            if (cm.GlobalIndividualActivityCache == null)
            {
                model.Error = Ressources.ApiResource.DataNotYetPopulated;
                return Json(JsonConvert.SerializeObject(model));
            }

            var CHARACTERS_PER_PAGE = 1000;
            if (pageInt < 1 || (pageInt - 1) * CHARACTERS_PER_PAGE >= cm.GlobalIndividualActivityCache.Count)
            {
                model.Error = Ressources.ApiResource.PageParameterOutOfBounds;
                return Json(JsonConvert.SerializeObject(model));
            }

            model.Timestamp = cm.LastGlobalIndividualActivityCache.ToString("yyyy-MM-ddTHH:mm:ss");
            int count = Math.Min(cm.GlobalIndividualActivityCache.Count - (pageInt - 1) * CHARACTERS_PER_PAGE, CHARACTERS_PER_PAGE);
            model.Characters = cm.GlobalIndividualActivityCache.GetRange((pageInt - 1) * CHARACTERS_PER_PAGE, count);
            model.MaxPage = (int) Math.Ceiling((double) cm.GlobalIndividualActivityCache.Count / CHARACTERS_PER_PAGE);
            return Json(JsonConvert.SerializeObject(model));
        }

        // GET: /<controller>/
        [HttpGet("{key}")]
        public JsonResult AdminGetPlayers(string key)
        {
            var model = new PlayersOnline<PlayerOnlineAdmin>();
            if (!isValidKey(key, true))
            {
                logger.LogWarning("Illegal access attempt with key: " + key, ", ip: " + HttpContext.Request.Host);
                model.Error = Ressources.ApiResource.UnauthorizedAccess;
                return Json(model);
            }

            model.Timestamp = CacheManager.Instance.LastUpdate.ToString("yyyy-MM-ddTHH:mm:ss");
            model.Players = new List<PlayerOnlineAdmin>();
            var players = context.ServerSessions.Include(c => c.ServerSessionsDataConn).Where(c => !c.SessionEnd.HasValue).ToList();
            var systems = context.Systems.ToList();
            var regions = context.Regions.ToList();

            foreach (var item in players)
            {
                var player = new PlayerOnlineAdmin();
                player.Name = item.PlayerName;

                //Last location data
                var last_system = item.ServerSessionsDataConn.LastOrDefault();
                if (last_system != null)
                {
                    var system = systems.SingleOrDefault(c => c.Nickname == last_system.Location.ToUpper()) ?? null;

                    //This is the always expected scenario
                    player.System = system != null ? system.Name : "Unknown";
                    player.Region = system != null ? regions.SingleOrDefault(c => c.Id == system.RegionId)?.Name ?? "Unknown" : "Unknown";
                }
                else
                {
                    //But better be safe than sorry
                    player.System = "ERROR";
                    player.Region = "ERROR";
                }

                player.ID = item.PlayerId;
                player.Ship = item.PlayerLastShip;
                player.IP = item.SessionIp;

                model.Players.Add(player);
            }

            return Json(JsonConvert.SerializeObject(model));
        }

        [HttpGet("{tag}/{key}")]
        public JsonResult GetFactionDetails(string tag, string key)
        {
            var model = new FactionDetailsModel();
            if (!isValidKey(key))
            {
                logger.LogWarning("Illegal access attempt with key: " + key, ", ip: " + HttpContext.Request.Host);
                model.Error = Ressources.ApiResource.UnauthorizedAccess;
                return Json(model);
            }

            // Kestrel is retarded and decodes most stuff, just not the forward slash character which is used in Auxesia's tag of A/)-
            tag = tag.Replace("%2F", "/");

            var now = DateTime.UtcNow;
            CacheManager cm = CacheManager.Instance;

            var factions = context.ServerFactions.Where(c => c.FactionTag == tag);
            if (factions.Count() == 0) {
                model.Error = Ressources.ApiResource.FactionNotFound;
                return Json(model);
            }
            var faction = factions.First();

            // Check if we have to renew the cache
            if (!cm.FactionIndividualActivityCache.ContainsKey(tag) || cm.FactionIndividualActivityCache[tag].LastCache.AddSeconds(cm.FactionIndividualCacheDuration) < now)
            {
                var start_now = new DateTime(now.Year, now.Month, 1, 0, 0, 0, 0);

                var start_last = start_now.AddMonths(-1);
                var end_last = new DateTime(start_last.Year, start_last.Month, DateTime.DaysInMonth(start_last.Year, start_last.Month), 23, 59, 59, 999);

                model.Timestamp = now.ToString("yyyy-MM-ddTHH:mm:ss");
                model.Characters = new Dictionary<string, CharacterActivity>();

                Dictionary<string, ulong> curr_time = new Dictionary<string, ulong>();
                Dictionary<string, ulong> last_time = new Dictionary<string, ulong>();

                //Get all sessions for the current month
                var sessions = context.ServerSessions.Include(c => c.ServerSessionsDataConn).Where(c => (((faction.FactionTag == "[TBH]" || faction.FactionTag == "|Aoi") && c.PlayerName.Contains(faction.FactionTag)) || c.PlayerName.StartsWith(faction.FactionTag)) && c.SessionStart >= start_now && c.SessionStart <= now && c.SessionEnd.HasValue).ToList();
                foreach (var session in sessions)
                {
                    model.Characters[session.PlayerName] = new CharacterActivity();
                    curr_time[session.PlayerName] = 0;
                }
                foreach (var session in sessions)
                {
                    foreach (var system in session.ServerSessionsDataConn)
                    {
                        if (!cm.WastedActivitySystems.Contains(system.Location.ToUpper()))
                        {
                            curr_time[session.PlayerName] += (ulong)system.Duration;
                        }
                    }
                }
                sessions = context.ServerSessions.Include(c => c.ServerSessionsDataConn).Where(c => (((faction.FactionTag == "[TBH]" || faction.FactionTag == "|Aoi") && c.PlayerName.Contains(faction.FactionTag)) || c.PlayerName.StartsWith(faction.FactionTag)) && c.SessionStart >= start_last && c.SessionStart <= end_last && c.SessionEnd.HasValue).ToList();
                foreach (var session in sessions)
                {
                    model.Characters[session.PlayerName] = new CharacterActivity();
                    last_time[session.PlayerName] = 0;
                }
                foreach (var session in sessions)
                {
                    foreach (var system in session.ServerSessionsDataConn)
                    {
                        if (!cm.WastedActivitySystems.Contains(system.Location.ToUpper()))
                        {
                            last_time[session.PlayerName] += (ulong)system.Duration;
                        }
                    }
                }

                foreach (KeyValuePair<string, CharacterActivity> entry in model.Characters)
                {
                    //Compile the data
                    ulong curr_seconds = 0;
                    ulong last_seconds = 0;
                    if (curr_time.ContainsKey(entry.Key)) {
                        curr_seconds = curr_time[entry.Key];
                    }
                    if (last_time.ContainsKey(entry.Key)) {
                        last_seconds = last_time[entry.Key];
                    }
                    entry.Value.Current_Time = FormatTime(curr_seconds);
                    entry.Value.Last_Time = FormatTime(last_seconds);
                }

                var cache = new FactionCache();
                cache.Cache = JsonConvert.SerializeObject(model);
                cache.LastCache = DateTime.UtcNow;
                cm.FactionIndividualActivityCache[tag] = cache;
            }

            return Json(cm.FactionIndividualActivityCache[tag].Cache);
        }

        [HttpGet("{key}")]
        public JsonResult GetFactionSummary(string key)
        {
            var model = new FactionSummaryModel();
            if (!isValidKey(key))
            {
                logger.LogWarning("Illegal access attempt with key: " + key, ", ip: " + HttpContext.Request.Host);
                model.Error = Ressources.ApiResource.UnauthorizedAccess;
                return Json(model);
            }

            var now = DateTime.UtcNow;
            CacheManager cm = CacheManager.Instance;

            //Check if we have to renew the cache
            if (cm.LastFactionGlobalActivityCache.AddSeconds(cm.FactionGlobalActivityDuration) < now)
            {
                model.Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
                model.Factions = new List<FactionSummarySingle>();
                
                var start_now = new DateTime(now.Year, now.Month, 1, 0, 0, 0, 0);

                var start_last = start_now.AddMonths(-1);
                var end_last = new DateTime(start_last.Year, start_last.Month, DateTime.DaysInMonth(start_last.Year, start_last.Month), 23, 59, 59, 999);

                var Factions = context.ServerFactions.ToList();
                foreach (var faction in Factions)
                {
                    var FactionMdl = new FactionSummarySingle();
                    FactionMdl.Name = faction.FactionName;
                    FactionMdl.Tag = faction.FactionTag;
                    FactionMdl.Danger = false;
                    FactionMdl.Id = faction.Id;

                    ulong curr_time = 0;
                    ulong last_time = 0;
                    
                    //Potentially existing activity records
                    var FactionActivity = context.ServerFactionsActivity.Where(c => c.FactionId == faction.Id).ToList();

                    //Get all sessions for the current month
                    var sessions = context.ServerSessions.Include(c => c.ServerSessionsDataConn).Where(c => (((faction.FactionTag == "[TBH]" || faction.FactionTag == "|Aoi") && c.PlayerName.Contains(faction.FactionTag)) || c.PlayerName.StartsWith(faction.FactionTag)) && c.SessionStart >= start_now && c.SessionStart <= now && c.SessionEnd.HasValue).ToList();
                    foreach (var item in sessions)
                    {
                        foreach (var system in item.ServerSessionsDataConn)
                        {
                            if (!cm.WastedActivitySystems.Contains(system.Location.ToUpper()))
                            {
                                //not wasted
                                curr_time += (ulong)system.Duration;
                            }
                        }
                    }

                    //Get the sessions of the previous month
                    if (FactionActivity.Any(c => c.Stamp == start_last))
                    {
                        var activity = FactionActivity.SingleOrDefault(c => c.Stamp == start_last);
                        last_time = activity.Duration;
                    }
                    else
                    {
                        //The values have not yet been precalculated
                        // I feel very hesitant to allow recalculations to be performed here, so I will not do it for now
                        sessions = context.ServerSessions.Include(c => c.ServerSessionsDataConn).Where(c => (((faction.FactionTag == "[TBH]" || faction.FactionTag == "|Aoi") && c.PlayerName.Contains(faction.FactionTag)) || c.PlayerName.StartsWith(faction.FactionTag)) && c.SessionStart >= start_last && c.SessionStart <= end_last && c.SessionEnd.HasValue).ToList();
                        foreach (var item in sessions)
                        {
                            foreach (var system in item.ServerSessionsDataConn)
                            {
                                if (!cm.WastedActivitySystems.Contains(system.Location.ToUpper()))
                                {
                                    //not wasted
                                    last_time += (ulong)system.Duration;
                                }
                            }
                        }
                    }

                    //Compile the data
                    FactionMdl.Current_Time = FormatTime(curr_time);
                    FactionMdl.Last_Time = FormatTime(last_time);

                    if (curr_time < cm.Faction_DangerThreshold)
                        FactionMdl.Danger = true;

                    model.Factions.Add(FactionMdl);

                }

                cm.FactionGlobalActivityCache = JsonConvert.SerializeObject(model);
                cm.LastFactionGlobalActivityCache = DateTime.UtcNow;
            }

            return Json(cm.FactionGlobalActivityCache);
        }

        private bool isValidKey(string key, bool requireAdmin = false)
        {
            if (context.ApiKeys.Any(c => c.Key == key && (!requireAdmin || c.Admin == true)))
                return true;
            else
                return false;
        }

        private string TimeIntToStr(int Time)
        {
            if (Time < 10)
                return "0" + Time.ToString();
            else
                return Time.ToString();
        }

        private string FormatTime(ulong seconds) {
            TimeSpan span = TimeSpan.FromSeconds(seconds);
            if (span.TotalHours < 24) {
                return string.Format("{0}:{1}:{2}", TimeIntToStr(span.Hours), TimeIntToStr(span.Minutes), TimeIntToStr(span.Seconds));
            } else {
                return string.Format("{0}d {1}:{2}:{3}", span.Days, TimeIntToStr(span.Hours), TimeIntToStr(span.Minutes), TimeIntToStr(span.Seconds));
            }
        }
    }
}
