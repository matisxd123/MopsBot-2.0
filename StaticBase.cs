using System;
using Discord;
using Discord.WebSocket;
using Discord.Commands;
using Discord.Rest;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;
using MopsBot.Data;
using MopsBot.Data.Tracker;
using Tweetinvi;
using Tweetinvi.Logic;
using MongoDB.Driver;
using MongoDB.Bson.Serialization.Options;
using MongoDB.Bson.Serialization.Attributes;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;

namespace MopsBot
{
    public class StaticBase
    {
        public static MongoClient DatabaseClient = new MongoClient($"{Program.Config["DatabaseURL"]}");
        public static IMongoDatabase Database = DatabaseClient.GetDatabase("Mops");
        public static readonly System.Net.Http.HttpClient HttpClient = new System.Net.Http.HttpClient();
        public static Random ran = new Random();
        public static Dictionary<BaseTracker.TrackerType, TrackerWrapper> Trackers;
        public static Server BotCommunication = new Server();

        public static bool init = false;

        /// <summary>
        /// Initialises and loads all trackers
        /// </summary>
        public static void initTracking()
        {
            if (!init)
            {
                HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MSIE 9.0; Windows NT 6.1; Trident/5.0)");
                ServicePointManager.ServerCertificateValidationCallback = (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => { return true; };
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
                HttpClient.DefaultRequestHeaders.ConnectionClose = true;
                HttpClient.Timeout = TimeSpan.FromSeconds(10);
                ServicePointManager.DefaultConnectionLimit = 100;
                ServicePointManager.MaxServicePointIdleTime = 10000;

                Auth.SetUserCredentials(Program.Config["TwitterKey"], Program.Config["TwitterSecret"],
                                        Program.Config["TwitterToken"], Program.Config["TwitterAccessSecret"]);
                TweetinviConfig.CurrentThreadSettings.TweetMode = TweetMode.Extended;
                TweetinviConfig.ApplicationSettings.TweetMode = TweetMode.Extended;
                Tweetinvi.ExceptionHandler.SwallowWebExceptions = false;
                Tweetinvi.RateLimit.RateLimitTrackerMode = RateLimitTrackerMode.TrackOnly;
                TweetinviEvents.QueryBeforeExecute += Data.Tracker.TwitterTracker.QueryBeforeExecute;
                Tweetinvi.Logic.JsonConverters.JsonPropertyConverterRepository.JsonConverters.Remove(typeof(Tweetinvi.Models.Language));
                Tweetinvi.Logic.JsonConverters.JsonPropertyConverterRepository.JsonConverters.Add(typeof(Tweetinvi.Models.Language), new CustomJsonLanguageConverter());

                Trackers = new Dictionary<BaseTracker.TrackerType, Data.TrackerWrapper>();
                Trackers[BaseTracker.TrackerType.Twitter] = new TrackerHandler<TwitterTracker>(1800000);
                Trackers[BaseTracker.TrackerType.Youtube] = new TrackerHandler<YoutubeTracker>(3600000);
                Trackers[BaseTracker.TrackerType.Twitch] = new TrackerHandler<TwitchTracker>(3600000);
                Trackers[BaseTracker.TrackerType.YoutubeLive] = new TrackerHandler<YoutubeLiveTracker>(900000);
                Trackers[BaseTracker.TrackerType.Mixer] = new TrackerHandler<MixerTracker>();
                Trackers[BaseTracker.TrackerType.Reddit] = new TrackerHandler<RedditTracker>();
                Trackers[BaseTracker.TrackerType.JSON] = new TrackerHandler<JSONTracker>(updateInterval: 600000);
                Trackers[BaseTracker.TrackerType.Osu] = new TrackerHandler<OsuTracker>();
                Trackers[BaseTracker.TrackerType.Overwatch] = new TrackerHandler<OverwatchTracker>(3600000);
                Trackers[BaseTracker.TrackerType.TwitchClip] = new TrackerHandler<TwitchClipTracker>();
                Trackers[BaseTracker.TrackerType.OSRS] = new TrackerHandler<OSRSTracker>();
                Trackers[BaseTracker.TrackerType.HTML] = new TrackerHandler<HTMLTracker>();
                Trackers[BaseTracker.TrackerType.RSS] = new TrackerHandler<RSSTracker>(3600000);
                Trackers[BaseTracker.TrackerType.Steam] = new TrackerHandler<SteamTracker>();

                foreach (var tracker in Trackers)
                {
                    var trackerType = tracker.Key;

                    if (tracker.Key == BaseTracker.TrackerType.Twitch)
                    {
                        Task.Run(() => TwitchTracker.ObtainTwitchToken());
                        Task.Run(() =>
                        {
                            tracker.Value.PostInitialisation();
                        });
                    }
                    else if (tracker.Key == BaseTracker.TrackerType.YoutubeLive)
                    {
                        Task.Run(() =>
                        {
                            tracker.Value.PostInitialisation();
                            YoutubeLiveTracker.fetchChannelsBatch().Wait();
                        });
                    }
                    else if (tracker.Key != BaseTracker.TrackerType.TwitchGroup)
                        Task.Run(() => tracker.Value.PostInitialisation());

                    Program.MopsLog(new LogMessage(LogSeverity.Info, "Tracker init", $"Initialising {trackerType.ToString()}"));
                    Task.Delay((int)(60000 / Trackers.Count)).Wait();
                }

                BotCommunication.StartServer();
            }
        }

        /// <summary>
        /// Displays the tracker counts one after another.
        /// </summary>
        /// <returns>A Task that sets the activity</returns>
        public static async Task UpdateStatusAsync()
        {
            if (!init)
            {
                try
                {
                    await SendHeartbeat();
                    await Task.Delay(30000);
                }
                catch { }

                DateTime LastGC = default(DateTime);
                while (true)
                {
                    try
                    {
                        //Collect garbage when over 2GB of RAM is used
                        if (((System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1024) / 1024) > 2000 && (DateTime.UtcNow - LastGC).TotalMinutes > 2)
                        {
                            await Program.MopsLog(new LogMessage(LogSeverity.Verbose, "", $"GC triggered."));
                            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                            System.GC.Collect();
                            LastGC = DateTime.UtcNow;
                        }
                    }
                    catch
                    {

                    }
                    finally
                    {
                        await SendHeartbeat();
                    }
                    await Task.Delay(30000);
                }
            }
        }

        public static async Task SendHeartbeat()
        {
            await Program.MopsLog(new LogMessage(LogSeverity.Verbose, "", $"Heartbeat. I am still alive :)\nRatio: {MopsBot.Module.Information.FailedRequests} failed vs {MopsBot.Module.Information.SucceededRequests} succeeded requests"));
            MopsBot.Module.Information.FailedRequests = 0;
            MopsBot.Module.Information.SucceededRequests = 0;
        }
    }
    public class CustomJsonLanguageConverter : Tweetinvi.Logic.JsonConverters.JsonLanguageConverter
    {
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, Newtonsoft.Json.JsonSerializer serializer)
        {
            return reader.Value != null
                ? base.ReadJson(reader, objectType, existingValue, serializer)
                : Tweetinvi.Models.Language.English;
        }
    }
}
