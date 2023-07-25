# WebNowPlaying-Redux Adapter Library
A C# library to communicate with the [WebNowPlaying-Redux](https://github.com/keifufu/WebNowPlaying-Redux) extension.  

Refer to [this](https://github.com/keifufu/WebNowPlaying-Redux/blob/main/CreatingAdapters.md) if you want to create or submit your own adapter.

## Installing
Available on [NuGet](https://www.nuget.org/packages/WNPRedux-Adapter-Library/) or as a dll in [Releases](https://github.com/keifufu/WNPRedux-Adapter-Library/releases)

## Native APIs
No built-in support for [Native APIs](https://github.com/keifufu/WebNowPlaying-Redux/blob/main/NativeAPIs.md).  
However, enough is exposed to make it work, get inspiration from [here](https://github.com/keifufu/WebNowPlaying-Redux-Rainmeter/blob/main/src/WNPReduxAdapterLibraryExtensions/WNPReduxNative.cs)

## Usage
```CS
using WNPReduxAdapterLibrary;
using System.Threading;
using System;

void Main() {
  // Custom logger
  void logger(WNPRedux.LogType type, string message) {
    Console.WriteLine($"{type}: {message}");
  }

  // Start WNP, providing a websocket port, version number and a logger
  WNPRedux.Start(1234, "1.0.0", logger);

  // Write the current title to the console and pause/unpause the video for 30 seconds
  for (int i = 0; i < 30; i++) {
    Console.WriteLine(WNPRedux.MediaInfo.Title);
    WNPRedux.MediaInfo.Controls.TryTogglePlayPause();
    Thread.Sleep(1000);
  }

  // Stop WNP
  WNPRedux.Stop();
}
```

## Documentation

### `WNPRedux.Start()`
`WNPRedux.Start(int port, string version, Action<LogType, string> logger, bool throttleLogs = false, string listenAddress = "127.0.0.1")`  
Starts WNP if it isn't already started.  
`port` should _not_ be used by other adapters already, or interfere with any other programs.  
`version` has to be "x.x.x".  
`throttleLogs` prevents the same log message being logged more than once per 30 seconds.

---

### `WNPRedux.isStarted`
Whether WNPRedux is started or not.

---

### `WNPRedux.isUsingNativeAPIs`
Whether WNPRedux is pulling info from native APIs.  
This is read-only, the actual value is set by the user.  
It's toggled in the browser extensions settings panel.  
Read more about it [here](https://github.com/keifufu/WebNowPlaying-Redux/blob/main/NativeAPIs.md).

---

### `WNPRedux.Log(WNPRedux.LogType type, string message)`
Calls the `logger` provided in `WNPRedux.Start()`  
Useful since it's throttled to one similar message per 30 seconds if `throttleLogs` is set to `true` in `WNPRedux.Start()`

---

### `WNPRedux.Stop()`
Stops WNP if it's started.

---

### `WNPRedux.clients`
Number of clients currently connected.  

---

### `WNPRedux.MediaInfo`
Information about the currently active media.
Name | Default | Description
--- | --- | ---
`Controls` | '' | Instance of MediaControls (Read below this table)
`PlayerName` | "" | Current player, e.g. YouTube, Spotify, etc.
`State` | STOPPED | Current state of the player (STOPPED, PLAYING, PAUSED) 
`Title` | "" | Title
`Artist` | "" | Artist
`Album` | "" | Album
`CoverUrl` | "" | URL to the cover image
`Duration` | "0:00" | Duration in (hh):mm:ss (Hours are optional)
`DurationSeconds` | 0 | Duration in seconds
`Position` | "0:00" | Position in (hh):mm:ss (Hours are optional)
`PositionSeconds` | 0 | Position in seconds
`PositionPercent` | 0.0 | Position in percent
`Volume` | 100 | Volume from 1-100
`Rating` | 0 | Rating from 0-5; Thumbs Up = 5; Thumbs Down = 1; Unrated = 0;
`RepeatMode` | NONE | Current repeat mode (NONE, ONE, ALL)
`ShuffleActive` | false | If shuffle is enabled

---

### `MediaControls`
Used in `WNPRedux.MediaInfo.controls`
Name  | Description
--- | ---
`supportsPlayPause` | If the current player supports play_pause
`supportsSkipPrevious` | If the current player supports skip_previous
`supportsSkipNext` | If the current player supports skip_next
`supportsSetPosition` | If the current player supports set_position
`supportsSetVolume` | If the current player supports set_volume
`supportsToggleRepeatMode` | If the current player supports toggle_repeat_mode
`supportsToggleShuffleActive` | If the current player supports toggle_shuffle_active
`supportsSetRating` | If the current player supports set_rating
`ratingSystem` | 'NONE' or 'LIKE_DISLIKE' or 'LIKE' or 'SCALE' (SCALE is 0-5)
`TryPlay()` | Tries to play the current media
`TryPause()` | Tries to pause the current media
`TryTogglePlayPause()` | Tries to play/pause the current media
`TrySkipPrevious()` | Try to skip to the previous media/section
`TrySkipNext()` | Try to skip to the next media/section
`TrySetPositionSeconds(int seconds)` | Try to set the medias playback progress in seconds
`TryRevertPositionSeconds(int seconds)` | Try to revert the medias playback progress by x seconds
`TryForwardPositionSeconds(int seconds)` | Try to forward the medias playback progress by x seconds
`TrySetPositionPercent(double percent)` | Try to set the medias playback progress in percent
`TryRevertPositionPercent(double percent)` | Try to revert the medias playback progress by x percent
`TryForwardPositionPercent(double percent)` | Try to forward the medias playback progress by x percent
`TrySetVolume(int volume)` | Try to set the medias volume from 1-100
`TryToggleRepeatMode()` | Try to toggle through repeat modes
`TryToggleShuffleActive()` | Try to toggle shuffle mode
`TrySetRating(int rating)` | Sites with a binary rating system fall back to: 0 = None; 1 = Thumbs Down; 5 = Thumbs Up

---

## Building from Source
You will need to open 'Turn Windows features on or off' and ensure that .NET Framework 3.5 is enabled, as pictured [here](https://oldimg.noonly.net/06BR2GT605.jpg).
