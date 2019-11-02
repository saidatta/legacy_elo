﻿using Discord;
using Discord.Commands;
using Discord.WebSocket;
using ELO.Models;
using ELO.Preconditions;
using ELO.Services;
using Newtonsoft.Json.Linq;
using RavenBOT.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ELO.Modules
{
    [RavenRequireContext(ContextType.Guild)]
    public partial class Info : ReactiveBase
    {
        public HttpClient HttpClient { get; }
        public CommandService CommandService { get; }
        public HelpService HelpService { get; }
        public GameService GameService { get; }

        public Info(HttpClient httpClient, CommandService commandService, HelpService helpService, GameService gameService)
        {
            HttpClient = httpClient;
            CommandService = commandService;
            HelpService = helpService;
            GameService = gameService;
        }

        [Command("Invite")]
        [Summary("Returns the bot invite")]
        public async Task InviteAsync()
        {
            await SimpleEmbedAsync($"Invite: https://discordapp.com/oauth2/authorize?client_id={Context.Client.CurrentUser.Id}&scope=bot&permissions=8");
        }

        [Command("Help")]
        [Summary("Shows available commands based on the current user permissions")]
        public async Task HelpAsync()
        {
            await GenerateHelpAsync();
        }

        [Command("FullHelp")]
        [Summary("Displays all commands without checking permissions")]
        public async Task FullHelpAsync()
        {
            await GenerateHelpAsync(false);
        }

        public async Task GenerateHelpAsync(bool checkPreconditions = true)
        {
            try
            {
                var res = await HelpService.PagedHelpAsync(Context, checkPreconditions, null, null);
                if (res != null)
                {
                    await PagedReplyAsync(res.ToCallBack().WithDefaultPagerCallbacks());
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        [Command("Shards")]
        [Summary("Displays information about all shards")]
        public async Task ShardInfoAsync()
        {
            var info = Context.Client.Shards.Select(x => $"[{x.ShardId}] {x.Status} {x.ConnectionState} - Guilds: {x.Guilds.Count} Users: {x.Guilds.Sum(g => g.MemberCount)}");
            await ReplyAsync($"```\n" + $"{string.Join("\n", info)}\n" + $"```");
        }

        [RateLimit(1, 1, Measure.Minutes, RateLimitFlags.ApplyPerGuild)]
        [Command("Stats")]
        [Summary("Bot Info and Stats")]
        public async Task InformationAsync()
        {
            string changes;
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/PassiveModding/ELO/commits");
            request.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; MSIE 10.0; Windows NT 6.2; WOW64; Trident/6.0)");
            var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                changes = "There was an error fetching the latest changes.";
            }
            else
            {
                dynamic result = JArray.Parse(await response.Content.ReadAsStringAsync());
                changes = $"[{((string)result[0].sha).Substring(0, 7)}]({result[0].html_url}) {result[0].commit.message}\n" + $"[{((string)result[1].sha).Substring(0, 7)}]({result[1].html_url}) {result[1].commit.message}\n" + $"[{((string)result[2].sha).Substring(0, 7)}]({result[2].html_url}) {result[2].commit.message}";
            }

            var embed = new EmbedBuilder();

            embed.WithAuthor(
                x =>
                {
                    x.IconUrl = Context.Client.CurrentUser.GetAvatarUrl();
                    x.Name = $"{Context.Client.CurrentUser.Username}'s Official Invite";
                    x.Url = $"https://discordapp.com/oauth2/authorize?client_id={Context.Client.CurrentUser.Id}&scope=bot&permissions=2146958591";
                });
            embed.AddField("Changes", changes.FixLength());

            embed.AddField("Members", $"Bot: {Context.Client.Guilds.Sum(x => x.Users.Count(z => z.IsBot))}\nHuman: {Context.Client.Guilds.Sum(x => x.Users.Count(z => !z.IsBot))}\nPresent: {Context.Client.Guilds.Sum(x => x.Users.Count(u => u.Status != UserStatus.Offline))}", true);
            embed.AddField("Members", $"Online: {Context.Client.Guilds.Sum(x => x.Users.Count(z => z.Status == UserStatus.Online))}\nAFK: {Context.Client.Guilds.Sum(x => x.Users.Count(z => z.Status == UserStatus.Idle))}\nDND: {Context.Client.Guilds.Sum(x => x.Users.Count(u => u.Status == UserStatus.DoNotDisturb))}", true);
            embed.AddField("Channels", $"Text: {Context.Client.Guilds.Sum(x => x.TextChannels.Count)}\nVoice: {Context.Client.Guilds.Sum(x => x.VoiceChannels.Count)}\nTotal: {Context.Client.Guilds.Sum(x => x.Channels.Count)}", true);
            embed.AddField("Guilds", $"Count: {Context.Client.Guilds.Count}\nTotal Users: {Context.Client.Guilds.Sum(x => x.MemberCount)}\nTotal Cached: {Context.Client.Guilds.Sum(x => x.Users.Count())}\n", true);
            var orderedShards = Context.Client.Shards.OrderByDescending(x => x.Guilds.Count).ToList();
            embed.AddField("Shards", $"Shards: {Context.Client.Shards.Count}\nMax: G:{orderedShards.First().Guilds.Count} ID:{orderedShards.First().ShardId}\nMin: G:{orderedShards.Last().Guilds.Count} ID:{orderedShards.Last().ShardId}", true);
            embed.AddField("Commands", $"Commands: {CommandService.Commands.Count()}\nAliases: {CommandService.Commands.Sum(x => x.Attributes.Count)}\nModules: {CommandService.Modules.Count()}", true);
            embed.AddField(":hammer_pick:", $"Heap: {Math.Round(GC.GetTotalMemory(true) / (1024.0 * 1024.0), 2)} MB\nUp: {(DateTime.Now - Process.GetCurrentProcess().StartTime).ToString(@"dd\D\ hh\H\ mm\M\ ss\S")}", true);
            embed.AddField(":beginner:", $"Written by: [PassiveModding](https://github.com/PassiveModding)\nDiscord.Net {DiscordConfig.Version}", true);

            await ReplyAsync("", false, embed.Build());
        }

        [Command("Ranks", RunMode = RunMode.Async)]
        [Summary("Displays information about the server's current ranks")]
        public async Task ShowRanksAsync()
        {
            using (var db = new Database())
            {
                var comp = db.GetOrCreateCompetition(Context.Guild.Id);
                var ranks = db.Ranks.Where(x => x.GuildId == Context.Guild.Id).ToList();
                if (ranks.Count == 0)
                {
                    await SimpleEmbedAsync("There are currently no ranks set up.", Color.Blue);
                    return;
                }

                var msg = ranks.OrderByDescending(x => x.Points).Select(x => $"{MentionUtils.MentionRole(x.RoleId)} - ({x.Points}) W: (+{x.WinModifier ?? comp.DefaultWinModifier}) L: (-{x.LossModifier ?? comp.DefaultLossModifier})").ToArray();
                await SimpleEmbedAsync(string.Join("\n", msg), Color.Blue);
            }
        }

        [Command("Profile", RunMode = RunMode.Async)] // Please make default command name "Stats"
        [Alias("Info", "GetUser")]
        [Summary("Displays information about you or the specified user.")]
        public async Task InfoAsync(SocketGuildUser user = null)
        {
            if (user == null)
            {
                user = Context.User as SocketGuildUser;
            }

            using (var db = new Database())
            {
                var player = db.Players.Find(Context.Guild.Id, user.Id);
                if (player == null)
                {
                    if (user.Id == Context.User.Id)
                    {
                        await SimpleEmbedAsync("You are not registered.", Color.DarkBlue);
                    }
                    else
                    {
                        await SimpleEmbedAsync("That user is not registered.", Color.Red);
                    }
                    return;
                }

                var ranks = db.Ranks.Where(x => x.GuildId == Context.Guild.Id).ToList();
                var maxRank = ranks.Where(x => x.Points < player.Points).OrderByDescending(x => x.Points).FirstOrDefault();
                string rankStr = null;
                if (maxRank != null)
                {
                    rankStr = $"Rank: {MentionUtils.MentionRole(maxRank.RoleId)} ({maxRank.Points})\n";
                }

                await SimpleEmbedAsync($"{player.GetDisplayNameSafe()} Stats\n" + // Use Title?
                            $"Points: {player.Points}\n" +
                            rankStr +
                            $"Wins: {player.Wins}\n" +
                            $"Losses: {player.Losses}\n" +
                            $"Draws: {player.Draws}\n" +
                            $"Games: {player.Games}\n" +
                            $"Registered At: {player.RegistrationDate.ToString("dd MMM yyyy")} {player.RegistrationDate.ToShortTimeString()}", Color.Blue);
            }

            //TODO: Add game history (last 5) to this response
            //+ if they were on the winning team?
            //maybe only games with a decided result should be shown?
        }

        [Command("Leaderboard", RunMode = RunMode.Async)]
        [Alias("lb", "top20")]
        [Summary("Shows the current server-wide leaderboard.")]
        //TODO: Ratelimiting as this is a data heavy command.
        public async Task LeaderboardAsync()
        {
            using (var db = new Database())
            {
                //TODO: Implement sort modes

                //Retrieve players in the current guild from database
                var users = db.Players.Where(x => x.GuildId == Context.Guild.Id);

                //Order players by score and then split them into groups of 20 for pagination
                var userGroups = users.OrderByDescending(x => x.Points).SplitList(20).ToArray();
                if (userGroups.Length == 0)
                {
                    await SimpleEmbedAsync("There are no registered users in this server yet.", Color.Blue);
                    return;
                }

                //Convert the groups into formatted pages for the response message
                var pages = GetPages(userGroups);

                //Construct a paginated message with each of the leaderboard pages
                await PagedReplyAsync(new ReactivePager(pages).ToCallBack().WithDefaultPagerCallbacks());
            }
        }

        public List<ReactivePage> GetPages(IEnumerable<Player>[] groups)
        {
            //Start the index at 1 because we are ranking players here ie. first place.
            int index = 1;
            var pages = new List<ReactivePage>(groups.Length);
            foreach (var group in groups)
            {
                var playerGroup = group.ToArray();
                var lines = GetPlayerLines(playerGroup, index);
                index = lines.Item1;
                var page = new ReactivePage();
                page.Color = Color.Blue;
                page.Title = $"{Context.Guild.Name} - Leaderboard";
                page.Description = lines.Item2;
                pages.Add(page);
            }

            return pages;
        }

        //Returns the updated index and the formatted player lines
        public (int, string) GetPlayerLines(Player[] players, int startValue)
        {
            var sb = new StringBuilder();

            //Iterate through the players and add their summary line to the list.
            foreach (var player in players)
            {
                sb.AppendLine($"{startValue}: {player.GetDisplayNameSafe()} - `{player.Points}`");
                startValue++;
            }

            //Return the updated start value and the list of player lines.
            return (startValue, sb.ToString());
        }

        private CommandInfo Command { get; set; }
        protected override void BeforeExecute(CommandInfo command)
        {
            Command = command;
            base.BeforeExecute(command);
        }
    }
}