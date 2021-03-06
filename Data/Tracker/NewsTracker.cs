/*using System;
using System.Collections.Generic;
using System.Linq;
using Discord;
using Discord.WebSocket;
using Discord.Commands;
using NewsAPI;
using NewsAPI.Constants;
using NewsAPI.Models;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MopsBot.Data.Tracker
{
    [MongoDB.Bson.Serialization.Attributes.BsonIgnoreExtraElements]
    public class NewsTracker : BaseTracker
    {
        public string LastNews, Query, Source;

        public NewsTracker() : base(600000, ExistingTrackers * 2000)
        {
        }

        public NewsTracker(Dictionary<string, string> args) : base(600000, 60000){
            base.SetBaseValues(args);
            Name = args["_Name"] + "|" + args["Query"];
            Query = args["Query"];

            //Check if Name ist valid
            try{
                var test = new NewsTracker(Name);
                test.Dispose();
                LastNews = test.LastNews;
            } catch (Exception e){
                this.Dispose();
                throw e;
            }

            if(StaticBase.Trackers[TrackerType.News].GetTrackers().ContainsKey(Name)){
                this.Dispose();

                args["Id"] = Name;
                var curTracker = StaticBase.Trackers[TrackerType.News].GetTrackers()[Name];
                curTracker.ChannelMessages[ulong.Parse(args["Channel"].Split(":")[1])] = args["Notification"];
                StaticBase.Trackers[TrackerType.News].UpdateContent(new Dictionary<string, Dictionary<string, string>>{{"NewValue", args}, {"OldValue", args}}).Wait();

                throw new ArgumentException($"Tracker for {args["_Name"]} existed already, updated instead!");
            }
        }

        public NewsTracker(string NewsQuery) : base(600000)
        {
            var request = NewsQuery.Split("|");
            Name = NewsQuery;

            Source = request[0];
            Query = request[1];

            //Check if query and source yield proper results, by forcing exceptions if not.
            try
            {
                var checkExists = getNews().Result;
                var test = checkExists[0];

                if (checkExists.GroupBy(x => x.Source.Name).Count() > 1)
                    throw new Exception();

                LastNews = test.PublishedAt.ToString();
            }
            catch (Exception)
            {
                Dispose();
                throw new Exception($"`{Source}` didn't yield any proper result{(Query.Equals("") ? "" : $" for `{Query}`")}.");
            }
        }

        protected async override void CheckForChange_Elapsed(object stateinfo)
        {
            try
            {
                Article[] newArticles = await getNews();

                if (newArticles.Length > 0)
                {
                    LastNews = newArticles.First().PublishedAt.ToString();
                    await StaticBase.Trackers[TrackerType.News].UpdateDBAsync(this);
                }

                foreach (Article newArticle in newArticles)
                {
                    foreach (ulong channel in ChannelMessages.Keys.ToList())
                    {
                        await OnMajorChangeTracked(channel, createEmbed(newArticle), ChannelMessages[channel]);
                    }
                }

            }
            catch (Exception e)
            {
                await Program.MopsLog(new LogMessage(LogSeverity.Error, "", $" error by {Name}", e));
            }
        }

        private async Task<Article[]> getNews()
        {
            var result = await StaticBase.NewsClient.GetEverythingAsync(new EverythingRequest()
            {
                Q = Query,
                Sources = new List<string>() { Source },
                From = DateTime.Parse(LastNews ?? DateTime.MinValue.ToUniversalTime().ToString()).AddSeconds(1),
                SortBy = SortBys.PublishedAt
            });

            return result.Articles.ToArray();
            //return result.Articles.Where(x => x.Title.ToUpper().Contains(Query.ToUpper())).ToArray();
        }

        private Embed createEmbed(Article article)
        {
            EmbedBuilder e = new EmbedBuilder();
            e.Color = new Color(255, 255, 255);
            e.Title = article.Title;
            e.Url = article.Url;
            e.Timestamp = article.PublishedAt;
            e.ThumbnailUrl = article.UrlToImage;

            EmbedFooterBuilder footer = new EmbedFooterBuilder();
            footer.IconUrl = "https://cdn5.vectorstock.com/i/1000x1000/82/39/the-news-icon-newspaper-symbol-flat-vector-5518239.jpg";
            footer.Text = article.Source.Name;
            e.Footer = footer;

            EmbedAuthorBuilder author = new EmbedAuthorBuilder();
            author.Name = article.Author ?? "Unknown Author";

            e.Author = author;

            e.Description = article.Description;

            return e.Build();
        }

        public override Dictionary<string, object> GetParameters(ulong guildId)
        {
            var parameters = base.GetParameters(guildId);
            (parameters["Parameters"] as Dictionary<string, object>)["_Name"] = "";
            (parameters["Parameters"] as Dictionary<string, object>)["Query"] = "";

            return parameters;
        }

        public override void Update(Dictionary<string, Dictionary<string, string>> args){
            base.Update(args);
            Query = args["NewValue"]["Query"];
        }

        public override object GetAsScope(ulong channelId){
            return new ContentScope(){
                Id = this.Name,
                _Name = this.Source,
                Query = this.Query,
                Notification = this.ChannelMessages[channelId],
                Channel = "#" + ((SocketGuildChannel)Program.Client.GetChannel(channelId)).Name + ":" + channelId
            };
        }

        public new struct ContentScope
        {
            public string Id;
            public string _Name;
            public string Query;
            public string Notification;
            public string Channel;
        }
    }
}*/