using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord.Commands;
using Discord;

namespace MopsBot.Module.Preconditions{
    /// <summary> Sets how often a user is allowed to use this command
    /// or any command in this module. </summary>
    /// <remarks>This is backed by an in-memory collection
    /// and will not persist with restarts.</remarks>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class RatelimitAttribute : PreconditionAttribute
    {
        private readonly uint _invokeLimit;
        private readonly bool _noLimitInDMs;
        private readonly bool _noLimitForAdmins;
        private readonly bool _applyPerGuild;
        private readonly bool _guildwideLimit;
        private readonly bool _applyPerChannel;
        private readonly bool _channelwideLimit;
        private readonly TimeSpan _invokeLimitPeriod;
        private readonly Dictionary<(ulong, ulong?), CommandTimeout> _invokeTracker = new Dictionary<(ulong, ulong?), CommandTimeout>();

        /// <summary> Sets how often a user is allowed to use this command. </summary>
        /// <param name="times">The number of times a user may use the command within a certain period.</param>
        /// <param name="period">The amount of time since first invoke a user has until the limit is lifted.</param>
        /// <param name="measure">The scale in which the <paramref name="period"/> parameter should be measured.</param>
        /// <param name="flags">Flags to set behavior of the ratelimit.</param>
        public RatelimitAttribute(
            uint times,
            double period,
            Measure measure,
            RatelimitFlags flags = RatelimitFlags.None)
        {
            _invokeLimit = times;
            _noLimitInDMs = (flags & RatelimitFlags.NoLimitInDMs) == RatelimitFlags.NoLimitInDMs;
            _noLimitForAdmins = (flags & RatelimitFlags.NoLimitForAdmins) == RatelimitFlags.NoLimitForAdmins;
            _applyPerGuild = (flags & RatelimitFlags.ApplyPerGuild) == RatelimitFlags.ApplyPerGuild;
            _guildwideLimit = (flags & RatelimitFlags.GuildwideLimit) == RatelimitFlags.GuildwideLimit;
            _applyPerChannel = (flags & RatelimitFlags.ApplyPerChannel) == RatelimitFlags.ApplyPerChannel;
            _channelwideLimit = (flags & RatelimitFlags.ChannelwideLimit) == RatelimitFlags.ChannelwideLimit;

            //TODO: C# 8 candidate switch expression
            switch (measure)
            {
                case Measure.Days:
                    _invokeLimitPeriod = TimeSpan.FromDays(period);
                    break;
                case Measure.Hours:
                    _invokeLimitPeriod = TimeSpan.FromHours(period);
                    break;
                case Measure.Minutes:
                    _invokeLimitPeriod = TimeSpan.FromMinutes(period);
                    break;
                case Measure.Seconds:
                    _invokeLimitPeriod = TimeSpan.FromSeconds(period);
                    break;
            }
        }

        /// <summary> Sets how often a user is allowed to use this command. </summary>
        /// <param name="times">The number of times a user may use the command within a certain period.</param>
        /// <param name="period">The amount of time since first invoke a user has until the limit is lifted.</param>
        /// <param name="flags">Flags to set bahavior of the ratelimit.</param>
        public RatelimitAttribute(
            uint times,
            TimeSpan period,
            RatelimitFlags flags = RatelimitFlags.None)
        {
            _invokeLimit = times;
            _noLimitInDMs = (flags & RatelimitFlags.NoLimitInDMs) == RatelimitFlags.NoLimitInDMs;
            _noLimitForAdmins = (flags & RatelimitFlags.NoLimitForAdmins) == RatelimitFlags.NoLimitForAdmins;
            _applyPerGuild = (flags & RatelimitFlags.ApplyPerGuild) == RatelimitFlags.ApplyPerGuild;
            _guildwideLimit = (flags & RatelimitFlags.GuildwideLimit) == RatelimitFlags.GuildwideLimit;
            _applyPerChannel = (flags & RatelimitFlags.ApplyPerChannel) == RatelimitFlags.ApplyPerChannel;
            _channelwideLimit = (flags & RatelimitFlags.ChannelwideLimit) == RatelimitFlags.ChannelwideLimit;

            _invokeLimitPeriod = period;
        }

        /// <inheritdoc />
        public override Task<PreconditionResult> CheckPermissionsAsync(
            ICommandContext context,
            CommandInfo command,
            IServiceProvider services)
        {
            if (_noLimitInDMs && context.Channel is IPrivateChannel)
                return Task.FromResult(PreconditionResult.FromSuccess());

            if (_noLimitForAdmins && context.User is IGuildUser gu && gu.GuildPermissions.Administrator)
                return Task.FromResult(PreconditionResult.FromSuccess());

            var now = DateTime.UtcNow;
            ulong scopeId = _applyPerChannel ? context.Channel.Id : _applyPerGuild ? context.Guild.Id : 0;
            ulong userId = _channelwideLimit ? context.Channel.Id : _guildwideLimit ? context.Guild.Id : context.User.Id;
            var key = (userId, scopeId);

            var timeout = (_invokeTracker.TryGetValue(key, out var t)
                && ((now - t.FirstInvoke) < _invokeLimitPeriod))
                    ? t : new CommandTimeout(now);

            timeout.TimesInvoked++;

            if (timeout.TimesInvoked <= _invokeLimit)
            {
                _invokeTracker[key] = timeout;
                return Task.FromResult(PreconditionResult.FromSuccess());
            }
            else
            {
                var timeLeft = _invokeLimitPeriod - (now - t.FirstInvoke);
                return Task.FromResult(PreconditionResult.FromError($"{(_guildwideLimit || _channelwideLimit ? "This command is" : "You are")} currently in Timeout.\nPlease try again in: `{timeLeft.Hours}h {timeLeft.Minutes}m {timeLeft.Seconds}s`"));
            }
        }

        public sealed class CommandTimeout
        {
            public uint TimesInvoked { get; set; }
            public DateTime FirstInvoke { get; }

            public CommandTimeout(DateTime timeStarted)
            {
                FirstInvoke = timeStarted;
            }
        }
    }

    /// <summary> Sets the scale of the period parameter. </summary>
    public enum Measure
    {
        /// <summary> Period is measured in days. </summary>
        Days,

        /// <summary> Period is measured in hours. </summary>
        Hours,

        /// <summary> Period is measured in minutes. </summary>
        Minutes,

        /// <summary> Period is measured in seconds. </summary>
        Seconds
    }

    /// <summary> Used to set behavior of the ratelimit </summary>
    [Flags]
    public enum RatelimitFlags
    {
        /// <summary> Set none of the flags. </summary>
        None = 0,

        /// <summary> Set whether or not there is no limit to the command in DMs. </summary>
        NoLimitInDMs = 1 << 0,

        /// <summary> Set whether or not there is no limit to the command for guild admins. </summary>
        NoLimitForAdmins = 1 << 1,

        /// <summary> Set whether or not to apply a limit per guild. </summary>
        ApplyPerGuild = 1 << 2,

        GuildwideLimit = 1 << 3 | 1 << 2,

        ApplyPerChannel = 1 << 4,

        ChannelwideLimit = 1 << 5 | 1 << 4
    }
}