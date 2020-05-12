using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using MopsBot.Data.Tracker;
using MongoDB.Driver;

namespace MopsBot.Data
{
    public abstract class TrackerWrapper
    {
        public abstract Task UpdateDBAsync(BaseTracker tracker);
        public abstract Task RemoveFromDBAsync(BaseTracker tracker);
        public abstract Task<bool> TryRemoveTrackerAsync(string name, ulong channelID);
        public abstract Task AddTrackerAsync(string name, ulong channelID, string notification = "");
        public abstract Task<Embed> GetEmbed();
        public abstract HashSet<Tracker.BaseTracker> GetTrackerSet();
        public abstract Dictionary<string, Tracker.BaseTracker> GetTrackers();
        public abstract IEnumerable<BaseTracker> GetTrackers(ulong channelID);
        public abstract Type GetTrackerType();
        public abstract void PostInitialisation();
    }

    /// <summary>
    /// A class containing all Trackers
    /// </summary>
    public class TrackerHandler<T> : TrackerWrapper where T : Tracker.BaseTracker
    {
        public Dictionary<string, T> trackers;
        public DatePlot IncreaseGraph;
        private int trackerInterval, updateInterval;
        private System.Threading.Timer nextTracker, nextUpdate;
        public TrackerHandler(int trackerInterval = 900000, int updateInterval = 120000)
        {
            this.trackerInterval = trackerInterval;
            this.updateInterval = updateInterval;
        }

        public override void PostInitialisation()
        {
            var collection = StaticBase.Database.GetCollection<T>(typeof(T).Name).FindSync<T>(x => true).ToList();
            IncreaseGraph = StaticBase.Database.GetCollection<DatePlot>("TrackerHandler").FindSync<DatePlot>(x => x.ID.Equals(typeof(T).Name + "Handler")).FirstOrDefault();
            IncreaseGraph?.InitPlot("Date", "Tracker Increase", "dd-MMM", false);

            trackers = collection.ToDictionary(x => x.Name);

            trackers = (trackers == null ? new Dictionary<string, T>() : trackers);

            if (collection.Count > 0)
            {
                int gap = trackerInterval / collection.Count;

                for (int i = trackers.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        var cur = trackers[trackers.Keys.ElementAt(i)];
                        //cur.SetTimer(trackerInterval, gap * (i + 1) + 20000);
                        bool save = cur.ChannelConfig.Count == 0;
                        cur.Conversion(trackers.Count - i);
                        cur.PostInitialisation(trackers.Count - i);
                        if (save) UpdateDBAsync(cur).Wait();
                        cur.OnMinorEventFired += OnMinorEvent;
                        cur.OnMajorEventFired += OnMajorEvent;
                    }
                    catch (Exception e)
                    {
                        Program.MopsLog(new LogMessage(LogSeverity.Error, "", $"Error on PostInitialisation, {e.Message}", e));
                    }
                }

                //Start Twitter STREAM after all are initialised
                if (typeof(T) == typeof(TwitterTracker))
                {
                    TwitterTracker.STREAM.StreamStopped += (sender, args) => { Program.MopsLog(new LogMessage(LogSeverity.Info, "", $"TwitterSTREAM stopped. {args.DisconnectMessage?.Reason ?? ""}", args.Exception)); TwitterTracker.RestartStream(); };
                    TwitterTracker.STREAM.StreamStarted += (sender, args) => Program.MopsLog(new LogMessage(LogSeverity.Info, "", "TwitterSTREAM started."));
                    TwitterTracker.STREAM.WarningFallingBehindDetected += (sender, args) => Program.MopsLog(new LogMessage(LogSeverity.Warning, "", $"TwitterSTREAM falling behind, {args.WarningMessage.Message} ({args.WarningMessage.PercentFull}%)"));
                    TwitterTracker.STREAM.FilterLevel = Tweetinvi.Streaming.Parameters.StreamFilterLevel.Low;
                    TwitterTracker.STREAM.StartStreamMatchingAllConditionsAsync();
                }
            }

            nextTracker = new System.Threading.Timer(LoopTrackers);
            loopQueue = trackers.Values.ToList();
            if (trackers.FirstOrDefault().Value is BaseUpdatingTracker)
            {
                nextUpdate = new System.Threading.Timer(LoopTrackersUpdate);
                loopQueue = trackers.Where(x => (x.Value as BaseUpdatingTracker).ToUpdate.Count == 0).Select(x => x.Value).ToList();
                updateQueue = trackers.Where(x => (x.Value as BaseUpdatingTracker).ToUpdate.Count > 0).Select(x => x.Value).ToList();
                nextUpdate.Change(5000, updateInterval / (updateQueue.Count > 0 ? updateQueue.Count : 1));
            }
            nextTracker.Change(5000, trackerInterval / (loopQueue.Count > 0 ? loopQueue.Count : 1));
        }

        private int trackerTurn;
        private List<T> loopQueue;
        public async void LoopTrackers(object state)
        {
            if (trackerTurn < loopQueue.Count)
            {
                BaseTracker curTracker = null;
                try
                {
                    curTracker = loopQueue[trackerTurn];
                    trackerTurn++;
                    curTracker.CheckForChange_Elapsed(null);
                }
                catch (Exception e)
                {
                    await Program.MopsLog(new LogMessage(LogSeverity.Error, "", $"Error on checking for change for {curTracker?.Name ?? "Unknown"}", e));
                }
            }

            else
            {
                if (trackers.FirstOrDefault().Value is BaseUpdatingTracker)
                {
                    loopQueue = trackers.Where(x => (x.Value as BaseUpdatingTracker).ToUpdate.Count == 0).Select(x => x.Value).ToList();
                }
                else
                {
                    loopQueue = trackers.Values.ToList();
                }
                var gap = trackerInterval / (loopQueue.Count > 0 ? loopQueue.Count : 1);
                nextTracker.Change(gap, gap);
                trackerTurn = 0;
            }
        }

        private int updateTurn;
        private List<T> updateQueue;
        public async void LoopTrackersUpdate(object state)
        {
            if (updateTurn < updateQueue.Count)
            {
                BaseTracker curTracker = null;
                try
                {
                    curTracker = updateQueue[updateTurn];
                    updateTurn++;
                    curTracker.CheckForChange_Elapsed(null);
                }
                catch (Exception e)
                {
                    await Program.MopsLog(new LogMessage(LogSeverity.Error, "", $"Error on checking for change for {curTracker?.Name ?? "Unknown"}", e));
                }
            }

            else{
                updateQueue = trackers.Where(x => (x.Value as BaseUpdatingTracker).ToUpdate.Count > 0).Select(x => x.Value).ToList();
                var gap = updateInterval / (updateQueue.Count > 0 ? updateQueue.Count : 1);
                nextUpdate.Change(gap, gap);
                updateTurn = 0;
            }
        }

        public override async Task UpdateDBAsync(BaseTracker tracker)
        {
            lock (tracker.ChannelConfig)
            {
                try
                {
                    StaticBase.Database.GetCollection<BaseTracker>(typeof(T).Name).ReplaceOneAsync(x => x.Name.Equals(tracker.Name), tracker, new UpdateOptions { IsUpsert = true }).Wait();
                }
                catch (Exception e)
                {
                    Program.MopsLog(new LogMessage(LogSeverity.Error, "", $"Error on upsert for {tracker.Name}, {e.Message}", e)).Wait();
                }
            }
        }

        public override async Task RemoveFromDBAsync(BaseTracker tracker)
        {
            lock (tracker.ChannelConfig)
            {
                try
                {
                    StaticBase.Database.GetCollection<T>(typeof(T).Name).DeleteOneAsync(x => x.Name.Equals(tracker.Name)).Wait();
                }
                catch (Exception e)
                {
                    Program.MopsLog(new LogMessage(LogSeverity.Error, "", $"Error on removing for {tracker.Name}, {e.Message}", e)).Wait();
                }
            }
        }

        public override async Task<bool> TryRemoveTrackerAsync(string name, ulong channelId)
        {
            if (trackers.ContainsKey(name) && trackers[name].ChannelConfig.ContainsKey(channelId))
            {
                if (trackers[name].ChannelConfig.Keys.Count > 1)
                {
                    trackers[name].ChannelConfig.Remove(channelId);

                    if (trackers.FirstOrDefault().Value is BaseUpdatingTracker)
                    {
                        (trackers[name] as BaseUpdatingTracker).ToUpdate.Remove(channelId);
                    }

                    await UpdateDBAsync(trackers[name]);
                    await Program.MopsLog(new LogMessage(LogSeverity.Info, "", $"Removed a {typeof(T).FullName} for {name}\nChannel: {channelId}"));
                }

                else
                {
                    var worked = loopQueue.Remove(trackers[name]);
                    var uWorked = updateQueue?.Remove(trackers[name]) ?? false;
                    trackers[name].Dispose();
                    await RemoveFromDBAsync(trackers[name]);
                    trackers.Remove(name);
                    await Program.MopsLog(new LogMessage(LogSeverity.Info, "", $"Removed a {typeof(T).FullName} for {name}\nChannel: {channelId}; Last channel left."));
                    await updateGraph(-1);
                }

                return true;
            }
            return false;
        }

        public override async Task AddTrackerAsync(string name, ulong channelID, string notification = "")
        {
            if (trackers.ContainsKey(name))
            {
                if (!trackers[name].ChannelConfig.ContainsKey(channelID))
                {
                    trackers[name].PostChannelAdded(channelID);
                    trackers[name].ChannelConfig[channelID]["Notification"] = notification;
                    await UpdateDBAsync(trackers[name]);
                }
            }
            else
            {
                var tracker = (T)Activator.CreateInstance(typeof(T), new object[] { name });
                name = tracker.Name;
                trackers.Add(name, tracker);
                tracker.PostChannelAdded(channelID);
                tracker.PostInitialisation();
                trackers[name].ChannelConfig[channelID]["Notification"] = notification;
                trackers[name].LastActivity = DateTime.Now;
                trackers[name].OnMajorEventFired += OnMajorEvent;
                trackers[name].OnMinorEventFired += OnMinorEvent;
                //trackers[name].SetTimer(trackerInterval);
                await UpdateDBAsync(trackers[name]);
                await updateGraph(1);
                loopQueue.Add(tracker);
            }

            await Program.MopsLog(new LogMessage(LogSeverity.Info, "", $"Started a new {typeof(T).Name} for {name}\nChannels: {string.Join(",", trackers[name].ChannelConfig.Keys)}\nMessage: {notification}"));
        }

        private async Task updateGraph(int increase = 1)
        {
            double dateValue = OxyPlot.Axes.DateTimeAxis.ToDouble(DateTime.Today);

            if (IncreaseGraph == null)
            {
                IncreaseGraph = new DatePlot(typeof(T).Name + "Handler", "Date", "Tracker Increase", "dd-MMM", false);
                IncreaseGraph.AddValue("Value", 0, DateTime.Today.AddMilliseconds(-1));
                IncreaseGraph.AddValue("Value", increase, DateTime.Today);
            }

            else
            {
                if (IncreaseGraph.PlotDataPoints.Last().Value.Key < dateValue)
                {
                    //Only show past year
                    IncreaseGraph.PlotDataPoints = IncreaseGraph.PlotDataPoints.SkipWhile(x => (DateTime.Today - OxyPlot.Axes.DateTimeAxis.ToDateTime(x.Value.Key)).Days >= 365).ToList();
                    for (int i = (int)(dateValue - IncreaseGraph.PlotDataPoints.Last().Value.Key) - 1; i > 0; i--)
                        IncreaseGraph.AddValue("Value", 0, DateTime.Today.AddDays(-i));
                    IncreaseGraph.AddValue("Value", increase, DateTime.Today);
                }
                else
                {
                    IncreaseGraph.AddValue("Value", IncreaseGraph.PlotDataPoints.Last().Value.Value + increase, DateTime.Today, replace: true);
                }
            }

            await StaticBase.Database.GetCollection<DatePlot>("TrackerHandler").ReplaceOneAsync(x => x.ID.Equals(typeof(T).Name + "Handler"), IncreaseGraph, new UpdateOptions { IsUpsert = true });
        }

        public override async Task<Embed> GetEmbed()
        {
            var embed = new EmbedBuilder();

            embed.WithTitle(typeof(T).Name + "Handler").WithDescription($"Currently harboring {this.trackers.Count} {typeof(T).Name}s");

            await updateGraph(0);
            embed.WithImageUrl(IncreaseGraph.DrawPlot());

            return embed.Build();
        }

        public override IEnumerable<BaseTracker> GetTrackers(ulong channelID)
        {
            return trackers.Select(x => x.Value).Where(x => x.ChannelConfig.ContainsKey(channelID));
        }

        public override Dictionary<string, BaseTracker> GetTrackers()
        {
            return trackers.Select(x => new KeyValuePair<string, BaseTracker>(x.Key, (BaseTracker)x.Value)).ToDictionary(x => x.Key, x => x.Value);
        }

        public override HashSet<BaseTracker> GetTrackerSet()
        {
            return trackers.Values.Select(x => (BaseTracker)x).ToHashSet();
        }

        public override Type GetTrackerType()
        {
            return typeof(T);
        }


        /// <summary>
        /// Event that is called when the Tracker fetches new data containing no Embed
        /// </summary>
        /// <returns>A Task that can be awaited</returns>
        private async Task OnMinorEvent(ulong channelID, Tracker.BaseTracker sender, string notification)
        {
            var message = new EventMessage(){
                ChannelId = channelID,
                Sender = sender.Name,
                Notification = notification
            };

            await StaticBase.BotCommunication.SendMessage(JsonConvert.SerializeObject(message));
        }

        /// <summary>
        /// Event that is called when the Tracker fetches new data containing an Embed
        /// Updates or creates the notification message with it
        /// </summary>
        /// <returns>A Task that can be awaited</returns>
        private async Task OnMajorEvent(ulong channelID, Embed embed, Tracker.BaseTracker sender, string notification)
        {
            var message = new EventMessage(){
                ChannelId = channelID,
                Embed = embed,
                Sender = sender.Name,
                Notification = notification
            };

            await StaticBase.BotCommunication.SendMessage(JsonConvert.SerializeObject(message));
        }
    }

    public struct EventMessage{
        public ulong ChannelId;
        public Embed Embed;
        public string Sender, Notification;
    }
}
