# WebNowPlaying-Redux Adapter Library
A C# library to communicate with the [WebNowPlaying-Redux](https://github.com/keifufu/WebNowPlaying-Redux) extension.

Refer to [this](https://github.com/keifufu/WebNowPlaying-Redux/blob/main/CreatingAdapters.md) if you want to create or submit your own adapter.

## Installing
Available on [NuGet](https://www.nuget.org/packages/WNPRedux-Adapter-Library/) or as a dll in [Releases](https://github.com/keifufu/WNPRedux-Adapter-Library/releases)

## Usage
```C#
using System;
using System.Threading;
using WNPReduxAdapterLibrary;

void Main() {
  // Custom logger
  void logger(WNPRedux.LogType type, string message) {
    Console.WriteLine($"{type}: {message}");
  }

  // Start the WebSocket, providing a port, version number and a logger
  WNPRedux.Initialize(1234, "1.0.0", logger);

  // Write the current title to the console and pause/unpause the video for 30 seconds
  for (int i = 0; i < 30; i++) {
    Console.WriteLine(WNPRedux.mediaInfo.Title);
    WNPRedux.mediaEvents.TogglePlaying();
    Thread.Sleep(1000);
  }

  // Close the WebSocket
  WNPRedux.Close();
}
```

### WNPRedux.Initialize()
`WNPRedux.Initialize(int port, string version, Action<LogType, string> logger, bool throttleLogs = false)`  
Opens the WebSocket if it isn't already opened.  
`port` should _not_ be used by other adapters already, or interfere with any other programs.  
`version` has to be "x.x.x".  
`throttleLogs` prevents the same log message being logged more than once per 30 seconds.

### WNPRedux.Log(WNPRedux.LogType type, string message)
Calls the `logger` provided in `WNPRedux.Initialize()`  
Useful since it's throttled to one similar message per 30 seconds if `throttleLogs` is set to `true` in `WNPRedux.Initialize()`

### WNPRedux.Close()
Closes the WebSocket if it's opened.

### WNPRedux.clients
Number of clients currently connected.  
Each client is normally a tab providing media information.

### WNPRedux.mediaInfo
Information about the currently active media.
Name | Default | Description
--- | --- | ---
`Player` | "" | Current player, e.g. YouTube, Spotify, etc.
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
`RepeatState` | NONE | Current repeat state (NONE, ONE, ALL)
`Shuffle` | false | If shuffle is enabled

### WNPRedux.mediaEvents
Events to interact with the currently active media.  
This isn't guaranteed to always work, since e.g. Spotify has no "dislike" button,  
skip buttons might be disabled in certain scenarios, etc.
Name  | Description
--- | ---
`TogglePlaying()` | Pauses / Unpauses the media
`Next()` | Skips to the next media/section
`Previous()` | Skips to the previous media/section
`SetPositionSeconds(int seconds)` | Sets the medias playback progress in seconds
`RevertPositionSeconds(int seconds)` | Reverts the medias playback progress by x seconds
`ForwardPositionSeconds(int seconds)` | Forwards the medias playback progress by x seconds
`SetPositionPercent(double percent)` | Sets the medias playback progress in percent
`RevertPositionPercent(double percent)` | Reverts the medias playback progress by x percent
`ForwardPositionPercent(double percent)` | Forwards the medias playback progress by x percent
`SetVolume(int volume)` | Set the medias volume from 1-100
`ToggleRepeat()` | Toggles through repeat modes
`ToggleShuffle()` | Toggles shuffle mode
`ToggleThumbsUp()` | Toggles thumbs up or similar
`ToggleThumbsDown()` | Toggles thumbs down or similar
`SetRating(int rating)` | Sites with a binary rating system fall back to: 0 = None; 1 = Thumbs Down; 5 = Thumbs Up

## Building from Source
You will need to open 'Turn Windows features on or off' and ensure that .NET Framework 3.5 is enabled, as pictured [here](https://oldimg.noonly.net/06BR2GT605.jpg).
