#pragma warning disable IDE1006
#pragma warning disable CS1591
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using WebSocketSharp.Server;
using System.Globalization;
using Newtonsoft.Json;
using WebSocketSharp;
using System.Linq;
using System.Net;
using System.IO;
using System;

namespace WNPReduxAdapterLibrary
{
  public class WNPRedux
  {
    private static bool _isStarted = false;
    /// <summary>
    /// Whether WNPRedux is started or not
    /// </summary>
    public static bool isStarted { get { return _isStarted; } }
    /// <summary>
    /// Info about the currently playing media.
    /// </summary>
    public static MediaInfo mediaInfo = new MediaInfo();
    private static WebSocketServer ws = null;
    /// <summary>
    /// Number of connected clients.
    /// </summary>
    public static int clients = 0;

    private static bool _isUsingNativeAPIs = false;
    /// <summary>
    /// Whether WNPRedux is pulling info from native APIs.  
    /// This is read-only, the actual value is set by the user.  
    /// It's opt-in through the browser extensions settings panel.  
    /// Read more about it here: https://github.com/keifufu/WebNowPlaying-Redux/blob/main/NativeAPIs.md
    /// </summary>
    public static bool isUsingNativeAPIs { get { return _isUsingNativeAPIs; } }
    private static readonly string enableNativeAPIsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "wnp_enable_native_api");

    private static readonly ConcurrentDictionary<string, MediaInfo>
        mediaInfoDictionary = new ConcurrentDictionary<string, MediaInfo>();

    public enum LogType { Debug, Warning, Error };
    private static Action<LogType, string> _logger;
    private static bool _throttleLogs = false;
    private static string adapterVersion = "";
    /// <summary>
    /// Starts WNP if it isn't already started.
    /// <returns>No return type</returns>
    /// <example>
    /// <code>
    /// void Logger(WNPRedux.LogType type, string message) {
    ///   Console.WriteLine($"{type}: {message}");
    /// }
    /// WNPRedux.Initialize(1234, "1.0.0", logger);
    /// </code>
    /// </example>
    /// </summary>
    /// <param name="wsPort">WebSocket Port</param>
    /// <param name="version">Adapter Version (major.minor.patch)</param>
    /// <param name="logger">Custom logger</param>
    /// <param name="throttleLogs">Prevent the same log message being logged more than once per 30 seconds</param>
    public static void Start(int wsPort, string version, Action<LogType, string> logger, bool throttleLogs = false)
    {
      try
      {
        if (_isStarted) return;
        _isStarted = true;
        _logger = logger;
        adapterVersion = version;
        _throttleLogs = throttleLogs;
        _isUsingNativeAPIs = Directory.Exists(enableNativeAPIsPath);
        mediaInfo = new MediaInfo();
        ws = new WebSocketServer(IPAddress.Loopback, wsPort);
        ws.AddWebSocketService<WNPReduxWebSocket>("/");
        ws.Start();
      }
      catch (Exception e)
      {
        _isStarted = false;
        Log(LogType.Error, "WNPRedux - Failed to start websocket");
        Log(LogType.Debug, $"WNPRedux - Error Trace: {e}");
      }
    }

    private static void SendMessage(string message)
    {
      if (ws == null || mediaInfo._ID.Length == 0) return;
      ws.WebSocketServices.TryGetServiceHost("/", out WebSocketServiceHost host);
      host.Sessions.SendTo(message, mediaInfo._ID);
    }

    /// <summary>
    /// Stops WNP if it's started.
    /// </summary>
    public static void Stop()
    {
      if (!_isStarted) return;
      _isStarted = false;
      if (ws != null)
      {
        ws.WebSocketServices.TryGetServiceHost("/", out WebSocketServiceHost host);
        foreach (string sessionID in host.Sessions.IDs)
        {
          host.Sessions.CloseSession(sessionID);
        }
        ws.Stop();
        ws = null;
      }
      _throttleLogs = false;
      mediaInfo = new MediaInfo();
    }

    private static readonly object _lock = new object();
    private static readonly Dictionary<string, int> _logCounts = new Dictionary<string, int>();
    private static readonly int _maxLogCount = 1;
    private static readonly TimeSpan _logResetTime = TimeSpan.FromSeconds(30);
    public static void Log(LogType type, string message)
    {
      if (!_throttleLogs)
      {
        _logger(type, message);
        return;
      }

      lock (_lock)
      {
        DateTime currentTime = DateTime.Now;
        string typeMessage = $"{type} {message}";

        if (!_logCounts.TryGetValue(typeMessage, out int logCount)) logCount = 0;

        if (logCount < _maxLogCount)
        {
          _logger(type, message);
          _logCounts[typeMessage] = logCount + 1;
          Task.Run(async () =>
          {
            await Task.Delay(_logResetTime);
            lock (_lock)
            {
              _logCounts.Remove(typeMessage);
            }
          });
        }
      }
    }

    private static void UpdateMediaInfo()
    {
      try
      {
        var iterableDictionary = mediaInfoDictionary
           .Where(kv => !kv.Value.IsNative || isUsingNativeAPIs)
           .OrderByDescending(kv => kv.Value.Timestamp);
        bool suitableMatch = false;

        foreach (KeyValuePair<string, MediaInfo> item in iterableDictionary)
        {
          // No need to check title since timestamp is only set when title is set
          if (item.Value.State == MediaInfo.StateMode.PLAYING && item.Value.Volume > 0)
          {
            mediaInfo = item.Value;
            suitableMatch = true;
            // If match found break early which should be always very early
            break;
          }
        }

        if (!suitableMatch)
        {
          MediaInfo fallbackInfo = iterableDictionary.FirstOrDefault().Value ?? new MediaInfo();
          mediaInfo = fallbackInfo;
        }
      }
      catch (Exception e)
      {
        Log(LogType.Error, "WNPRedux - Error finding new media info to display");
        Log(LogType.Debug, $"WNPRedux - Error Trace: {e}");
      }
    }

    private static string Pad(int num, int size)
    {
      return num.ToString().PadLeft(size, '0');
    }
    private static string TimeInSecondsToString(int timeInSeconds)
    {
      try
      {
        int timeInMinutes = (int)Math.Floor((double)timeInSeconds / 60);
        if (timeInMinutes < 60)
          return timeInMinutes.ToString() + ":" + Pad((timeInSeconds % 60), 2).ToString();

        return Math.Floor((double)timeInMinutes / 60).ToString() + ":" + Pad((timeInMinutes % 60), 2).ToString() + ":" + Pad((timeInSeconds % 60), 2).ToString();
      }
      catch
      {
        return "0:00";
      }
    }

    public class MediaInfo
    {
      private string _Title { get; set; }
      private StateMode _State { get; set; }
      private int _Volume { get; set; }

      public MediaInfo()
      {
        Controls = new MediaControls();
        _Title = "";
        _State = StateMode.STOPPED;
        _ID = "";
        PlayerName = "";
        Artist = "";
        Album = "";
        CoverUrl = "";
        Duration = "0:00";
        DurationSeconds = 0;
        Position = "0:00";
        PositionSeconds = 0;
        PositionPercent = 0;
        Volume = 100;
        Rating = 0;
        RepeatMode = RepeatModeEnum.NONE;
        ShuffleActive = false;
        Timestamp = 0;
      }

      public int TimestampOffsetInSeconds = 0;
      public enum StateMode { STOPPED, PLAYING, PAUSED }
      public enum RepeatModeEnum { NONE, ONE, ALL }
      public bool IsNative = false;
      public MediaControls Controls { get; set; }
      public StateMode State
      {
        get { return _State; }
        set
        {
          _State = value;
          Timestamp = (DateTime.Now.Ticks / (decimal)TimeSpan.TicksPerMillisecond) + TimestampOffsetInSeconds * 1000;
        }
      }
      public string _ID { get; set; }
      public string PlayerName { get; set; }
      public string Title
      {
        get { return _Title; }
        set
        {
          _Title = value;
          if (value.Length > 0) Timestamp = (DateTime.Now.Ticks / (decimal)TimeSpan.TicksPerMillisecond) + TimestampOffsetInSeconds;
          else Timestamp = 0;
        }
      }
      public string Artist { get; set; }
      public string Album { get; set; }
      public string CoverUrl { get; set; }
      public string Duration { get; set; }
      public int DurationSeconds { get; set; }
      public string Position { get; set; }
      public int PositionSeconds { get; set; }
      public double PositionPercent { get; set; }
      public int Volume
      {
        get { return _Volume; }
        set
        {
          _Volume = value;
          if (State == StateMode.PLAYING)
          {
            Timestamp = (DateTime.Now.Ticks / (decimal)TimeSpan.TicksPerMillisecond) + TimestampOffsetInSeconds;
          }
        }
      }
      public int Rating { get; set; }
      public RepeatModeEnum RepeatMode { get; set; }
      public bool ShuffleActive { get; set; }
      public decimal Timestamp { get; set; }
    }

    public class MediaControls
    {
      public MediaControls(bool supportsPlayPause = false, bool supportsSkipPrevious = false, bool supportsSkipNext = false, bool supportsSetPosition = false, bool supportsSetVolume = false, bool supportsToggleRepeatMode = false, bool supportsToggleShuffleActive = false, bool supportsSetRating = false, RatingSystem ratingSystem = RatingSystem.NONE)
      {
        this.supportsPlayPause = supportsPlayPause;
        this.supportsSkipPrevious = supportsSkipPrevious;
        this.supportsSkipNext = supportsSkipNext;
        this.supportsSetPosition = supportsSetPosition;
        this.supportsSetVolume = supportsSetVolume;
        this.supportsToggleRepeatMode = supportsToggleRepeatMode;
        this.supportsToggleShuffleActive = supportsToggleShuffleActive;
        this.supportsSetRating = supportsSetRating;
        this.ratingSystem = ratingSystem;
      }

      public static MediaControls fromJson(string jsonStr)
      {
        dynamic jsonData = JsonConvert.DeserializeObject(jsonStr);
        bool supportsPlayPause = jsonData["supports_play_pause"] ?? false;
        bool supportsSkipPrevious = jsonData["supports_skip_previous"] ?? false;
        bool supportsSkipNext = jsonData["supports_skip_next"] ?? false;
        bool supportsSetPosition = jsonData["supports_set_position"] ?? false;
        bool supportsSetVolume = jsonData["supports_set_volume"] ?? false;
        bool supportsToggleRepeatMode = jsonData["supports_toggle_repeat_mode"] ?? false;
        bool supportsToggleShuffleActive = jsonData["supports_toggle_shuffle_active"] ?? false;
        bool supportsSetRating = jsonData["supports_set_rating"] ?? false;
        RatingSystem ratingSystem = (RatingSystem)Enum.Parse(typeof(RatingSystem), jsonData["rating_system"].ToString() ?? "NONE");

        return new MediaControls(
          supportsPlayPause,
          supportsSkipPrevious,
          supportsSkipNext,
          supportsSetPosition,
          supportsSetVolume,
          supportsToggleRepeatMode,
          supportsToggleShuffleActive,
          supportsSetRating,
          ratingSystem
        );
      }

      public enum RatingSystem { NONE, LIKE, LIKE_DISLIKE, SCALE }
      public bool supportsPlayPause { get; set; }
      public bool supportsSkipPrevious { get; set; }
      public bool supportsSkipNext { get; set; }
      public bool supportsSetPosition { get; set; }
      public bool supportsSetVolume { get; set; }
      public bool supportsToggleRepeatMode { get; set; }
      public bool supportsToggleShuffleActive { get; set; }
      public bool supportsSetRating { get; set; }
      public RatingSystem ratingSystem { get; set; }

      private enum Events
      {
        TRY_SET_STATE,
        TRY_SKIP_PREVIOUS,
        TRY_SKIP_NEXT,
        TRY_SET_POSITION,
        TRY_SET_VOLUME,
        TRY_TOGGLE_REPEAT_MODE,
        TRY_TOGGLE_SHUFFLE_ACTIVE,
        TRY_SET_RATING
      }
      /// <summary>
      /// Tries to play the current media
      /// </summary>
      public void TryPlay() { SendMessage($"{Events.TRY_SET_STATE} PLAYING"); }
      /// <summary>
      /// Tries to pause the current media
      /// </summary>
      public void TryPause() { SendMessage($"{Events.TRY_SET_STATE} PAUSED"); }
      /// <summary>
      /// Tries to play/pause the current media
      /// </summary>
      public void TryTogglePlayPause()
      {
        if (mediaInfo.State == MediaInfo.StateMode.PLAYING) TryPause();
        else TryPlay();
      }
      /// <summary>
      /// Try to skip to the previous media/section
      /// </summary>
      public void TrySkipPrevious() { SendMessage($"{Events.TRY_SKIP_PREVIOUS}"); }
      /// <summary>
      /// Try to skip to the next media/section
      /// </summary>
      public void TrySkipNext() { SendMessage($"{Events.TRY_SKIP_NEXT}"); }
      /// <summary>
      /// Try to set the medias playback progress in seconds
      /// </summary>
      /// <param name="seconds"></param>.
      public void TrySetPositionSeconds(int seconds)
      {
        int positionInSeconds = seconds;
        if (positionInSeconds < 0) positionInSeconds = 0;
        if (positionInSeconds > mediaInfo.DurationSeconds) positionInSeconds = mediaInfo.DurationSeconds;
        double positionInPercent = (double)positionInSeconds / mediaInfo.DurationSeconds; // Doesn't cry about dividing by zero as it's a double
        // This makes sure it always gives us 0.0, not 0,0 (dot instead of comma, regardless of localization)
        string positionInPercentString = positionInPercent.ToString(CultureInfo.InvariantCulture);

        SendMessage($"{Events.TRY_SET_POSITION} {positionInSeconds}:{positionInPercentString}");
      }
      /// <summary>
      /// Try to revert the medias playback progress by x seconds
      /// </summary>
      /// <param name="seconds"></param>.
      public void TryRevertPositionSeconds(int seconds)
      {
        TrySetPositionSeconds(mediaInfo.PositionSeconds - seconds);
      }
      /// <summary>
      /// Try to forward the medias playback progress by x seconds
      /// </summary>
      /// <param name="seconds"></param>.
      public void TryForwardPositionSeconds(int seconds)
      {
        TrySetPositionSeconds(mediaInfo.PositionSeconds + seconds);
      }
      /// <summary>
      /// Try to set the medias playback progress in percent
      /// </summary>
      /// <param name="percent"></param>
      public void TrySetPositionPercent(double percent)
      {
        int seconds = (int)Math.Round((percent / 100) * mediaInfo.DurationSeconds);
        TrySetPositionSeconds(seconds);
      }
      /// <summary>
      /// Try to revert the medias playback progress by x percent
      /// </summary>
      /// <param name="percent"></param>.
      public void TryRevertPositionPercent(double percent)
      {
        int seconds = (int)Math.Round((percent / 100) * mediaInfo.DurationSeconds);
        TrySetPositionSeconds(mediaInfo.PositionSeconds - seconds);
      }
      /// <summary>
      /// Try to forward the medias playback progress by x percent
      /// </summary>
      /// <param name="percent"></param>.
      public void TryForwardPositionPercent(double percent)
      {
        int seconds = (int)Math.Round((percent / 100) * mediaInfo.DurationSeconds);
        TrySetPositionSeconds(mediaInfo.PositionSeconds + seconds);
      }
      /// <summary>
      /// Try to set the medias volume from 1-100
      /// </summary>
      /// <param name="volume">Number from 0-100</param>
      public void TrySetVolume(int volume)
      {
        int newVolume = volume;
        if (volume < 0) newVolume = 0;
        if (volume > 100) newVolume = 100;
        SendMessage($"{Events.TRY_SET_VOLUME} {newVolume}");
      }
      /// <summary>
      /// Try to toggle through repeat modes
      /// </summary>
      public void TryToggleRepeat() { SendMessage($"{Events.TRY_TOGGLE_REPEAT_MODE}"); }
      /// <summary>
      /// Try to toggle shuffle mode
      /// </summary>
      public void TryToggleShuffleActive() { SendMessage($"{Events.TRY_TOGGLE_SHUFFLE_ACTIVE}"); }
      /// <summary>
      /// Try to set the rating from 0-5 on websites that support it.
      /// Falls back to:
      /// 0 = no rating
      /// 1-2 = Thumbs Down
      /// 3-5 = Thumbs Up
      /// </summary>
      /// <param name="rating">Number from 0-5</param>
      public void TrySetRating(int rating) { SendMessage($"{Events.TRY_SET_RATING} {rating}"); }
    }

    private class WNPReduxWebSocket : WebSocketBehavior
    {
      protected override void OnMessage(MessageEventArgs arg)
      {
        try
        {
          string type = arg.Data.Substring(0, arg.Data.IndexOf(" ")).ToUpper();
          string info = arg.Data.Substring(arg.Data.IndexOf(" ") + 1);

          if (type == "USE_NATIVE_APIS")
          {
            _isUsingNativeAPIs = bool.Parse(info);
            if (isUsingNativeAPIs) { }
            bool state = bool.Parse(info);
            if (state && !Directory.Exists(enableNativeAPIsPath))
              Directory.CreateDirectory(enableNativeAPIsPath);
            else if (!state && Directory.Exists(enableNativeAPIsPath))
              Directory.Delete(enableNativeAPIsPath);
            UpdateMediaInfo();
            return;
          }

          MediaInfo currentMediaInfo = new MediaInfo();
          if (!mediaInfoDictionary.TryGetValue(ID, out currentMediaInfo))
          {
            // This should never happen but just in case do this to prevent getting an error
            currentMediaInfo = new MediaInfo();
            mediaInfoDictionary.GetOrAdd(ID, currentMediaInfo);
          }
          // I guess that TryGetValue can return an uninitialized value in some errors, so make sure it is good
          if (currentMediaInfo == null)
          {
            currentMediaInfo = new MediaInfo();
          }

          currentMediaInfo._ID = ID;

          if (type == "PLAYER_NAME") currentMediaInfo.PlayerName = info;
          else if (type == "IS_NATIVE") currentMediaInfo.IsNative = bool.Parse(info);
          else if (type == "TIMESTAMP_OFFSET_SECONDS") currentMediaInfo.TimestampOffsetInSeconds = Convert.ToInt16(info);
          else if (type == "PLAYER_CONTROLS") currentMediaInfo.Controls = MediaControls.fromJson(info);
          else if (type == "STATE") currentMediaInfo.State = (MediaInfo.StateMode)Enum.Parse(typeof(MediaInfo.StateMode), info);
          else if (type == "TITLE") currentMediaInfo.Title = info;
          else if (type == "ARTIST") currentMediaInfo.Artist = info;
          else if (type == "ALBUM") currentMediaInfo.Album = info;
          else if (type == "COVER_URL") currentMediaInfo.CoverUrl = info;
          else if (type == "DURATION_SECONDS")
          {
            currentMediaInfo.DurationSeconds = Convert.ToInt32(info);
            currentMediaInfo.Duration = TimeInSecondsToString(currentMediaInfo.DurationSeconds);
            // I guess set positionPercent to 0, because if duration changes, a new video is playing
            currentMediaInfo.PositionPercent = 0;
          }
          else if (type == "POSITION_SECONDS")
          {
            currentMediaInfo.PositionSeconds = Convert.ToInt32(info);
            currentMediaInfo.Position = TimeInSecondsToString(currentMediaInfo.PositionSeconds);

            if (currentMediaInfo.DurationSeconds > 0)
            {
              currentMediaInfo.PositionPercent = ((double)currentMediaInfo.PositionSeconds / currentMediaInfo.DurationSeconds) * 100.0;
            }
            else
            {
              currentMediaInfo.PositionPercent = 100;
            }
          }
          else if (type == "VOLUME") currentMediaInfo.Volume = Convert.ToInt16(info);
          else if (type == "RATING") currentMediaInfo.Rating = Convert.ToInt16(info);
          else if (type == "REPEAT_MODE") currentMediaInfo.RepeatMode = (MediaInfo.RepeatModeEnum)Enum.Parse(typeof(MediaInfo.RepeatModeEnum), info);
          else if (type == "SHUFFLE_ACTIVE") currentMediaInfo.ShuffleActive = bool.Parse(info);
          else if (type == "ERROR") Log(LogType.Error, $"WNPRedux - Browser Error: {info}");
          else if (type == "ERRORDEBUG") Log(LogType.Debug, $"WNPRedux - Browser Error Trace: {info}");
          else Log(LogType.Warning, $"WNPRedux - Unknown message type: {type}; ({arg.Data})");

          if (type != "POSITION" && currentMediaInfo.Title.Length > 0)
            UpdateMediaInfo();
        }
        catch (Exception e)
        {
          Log(LogType.Error, "WNPRedux - Error parsing data from WebNowPlaying-Redux");
          Log(LogType.Debug, $"WNPRedux - Error Trace: {e}");
        }
      }

      protected override void OnOpen()
      {
        base.OnOpen();
        clients++;
        Sessions.SendTo($"ADAPTER_VERSION {adapterVersion};WNPRLIB_REVISION 2", ID);
        mediaInfoDictionary.GetOrAdd(ID, new MediaInfo());
      }

      protected override void OnClose(CloseEventArgs e)
      {
        base.OnClose(e);
        clients--;

        mediaInfoDictionary.TryRemove(ID, out MediaInfo temp);
        if (mediaInfo._ID == temp._ID)
          UpdateMediaInfo();
      }
    }
  }
}

#pragma warning restore CS1591
#pragma warning restore IDE1006
