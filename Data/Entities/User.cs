﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using MongoDB.Driver;
using MongoDB.Bson.Serialization.Options;
using MongoDB.Bson.Serialization.Attributes;
using Discord;
using DiscordBotsList.Api.Objects;

namespace MopsBot.Data.Entities
{
    [BsonIgnoreExtraElements]
    public class User
    {
        private static List<DiscordBotsList.Api.Objects.IDblEntity> pastList;
        public static event UserHasVoted UserVoted;
        public delegate Task UserHasVoted(IDblEntity voter);

        [BsonId]
        public ulong Id;
        public int Money, Experience, Punched, Hugged, Kissed;
        public int WeaponId;
        public List<int> Inventory;

        private User(ulong pId)
        {
            Id = pId;
        }

        public int CalcExperience(int level)
        {
            return 200 * level * level;
        }

        public int CalcCurLevel()
        {
            return (int)Math.Sqrt(Experience / 200.0);
        }

        public static async Task<User> GetUserAsync(ulong id)
        {
            User user = (await StaticBase.Database.GetCollection<User>("Users").FindAsync(x => x.Id == id)).FirstOrDefault();

            if (user == null)
            {
                user = new User(id);
                await StaticBase.Database.GetCollection<User>("Users").InsertOneAsync(user);
            }

            return user;
        }

        public static async Task ModifyUserAsync(ulong id, Action<User> modification)
        {
            await (await GetUserAsync(id)).ModifyAsync(modification);
        }

        private async Task ModifyAsync(Action<User> modification)
        {
            modification(this);
            await StaticBase.Database.GetCollection<User>("Users").ReplaceOneAsync(x => x.Id == Id, this);
        }

        private string DrawProgressBar()
        {
            int Level = CalcCurLevel();
            double expCurrentHold = Experience - CalcExperience(Level);
            string output = "", TempOutput = "";
            double diffExperience = CalcExperience(Level + 1) - CalcExperience(Level);
            for (int i = 0; i < Math.Floor(expCurrentHold / (diffExperience / 10)); i++)
            {
                output += "■";
            }
            for (int i = 0; i < 10 - output.Length; i++)
            {
                TempOutput += "□";
            }
            return output + TempOutput;
        }

        public Embed StatEmbed()
        {
            EmbedBuilder e = new EmbedBuilder();
            e.WithAuthor(Program.Client.GetUser(Id).Username, Program.Client.GetUser(Id).GetAvatarUrl());
            e.WithCurrentTimestamp().WithColor(Discord.Color.Blue);

            e.AddField("Level", $"{CalcCurLevel()} ({Experience}/{CalcExperience(CalcCurLevel() + 1)}xp)\n{DrawProgressBar()}", true);
            e.AddField("Interactions", $"**Kissed** {Kissed} times\n**Hugged** {Hugged} times\n**Punched** {Punched} times", true);
            e.AddField("Votepoints", Money, false);

            return e.Build();
        }

        public static async Task CheckUsersVotedLoop()
        {
            while (true)
            {
                var voterList = await StaticBase.DiscordBotList.GetVotersAsync();
                voterList.Reverse();

                var newVoters = new List<IDblEntity>();

                if (pastList == null)
                    pastList = voterList.ToList();

                if (voterList.Count >= 999)
                {
                    int startIndex = pastList.FindIndex(x => x.Id == voterList[0].Id);

                    for (int i = startIndex; i < voterList.Count; i++)
                    {
                        if (pastList.Count < i || pastList[i].Id != voterList[i].Id)
                            newVoters.Add(voterList[i]);
                    }
                }
                else
                {
                    for (int i = pastList.Count; i < voterList.Count; i++)
                    {
                        newVoters.Add(voterList[i]);
                    }
                }

                pastList = voterList;

                if (UserVoted != null)
                    foreach (var user in newVoters)
                        await UserVoted.Invoke(user);

                await Task.Delay(60000);
            }
        }
    }
}
