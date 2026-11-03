# Windows Quotes

A small widget that sits on your Windows desktop, above the wallpaper and behind your windows, and shows a quote that changes every so often. Quotes come from your own list and from free online sources.

## Run it

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build it.

```powershell
dotnet publish src/WindowsQuotes -c Release -o publish
.\publish\WindowsQuotes.exe
```

To keep the app running after sign-in, turn on **Start with Windows** in Settings. It points at the executable's current location, so publish to the folder you plan to keep first. Running the published app needs the .NET 10 Desktop Runtime, which the SDK includes.

## Using it

The widget has a tray icon. You may need to click the `^` arrow in the taskbar to see it, and you can drag it out to keep it visible.

- **Drag the quote** to move it. The widget's centre stays where you drop it, and it always stays above the taskbar.
- **Left-click the tray icon** to manage quotes and settings.
- **Right-click the tray icon** for next quote, hold, add a quote, move widget and quit.
- **Star a quote** with the star button to keep it. My Quotes has separate tabs for quotes you added and quotes you starred.
- **Move widget** in the tray menu is only needed with tray-only controls, where the widget otherwise ignores the mouse.
- **Starting the app again** while it's running opens the manager.

### Holding a quote

Hold keeps the current quote on screen for 1 hour, 4 hours, until tomorrow, 1 week, or until you resume. Holds survive restarts. To end one early, choose **Resume rotation** to keep the quote until the next interval, or **Next** to move on straight away. In My Quotes, **Show and hold** puts any of your own quotes on screen and holds it.

### Settings

| Setting | Options |
|---|---|
| Look | Bare text on your wallpaper, frosted glass card, tinted dark card |
| Controls | Buttons on hover, right-click menu, tray icon only (clicks pass through) |
| Interval | 15 minutes, 30 minutes, 1 hour, 4 hours, 1 day |
| Your added and starred quotes | How often they show, from 0% to 100% |
| Also | Text size, monitor, reset position, start with Windows |

The widget sits in front of desktop icons, so place it in an empty area. In the bare text and card looks, it takes clicks across its whole rectangle unless you choose tray-only controls.

## Online sources

Each can be turned off in the Sources tab. Quotes are saved after each fetch, and the widget falls back to its saved quotes, then your own, then a small built-in set, so it works offline.

| Source | Notes |
|---|---|
| [ZenQuotes](https://zenquotes.io/) | Free tier, with attribution required (shown on the Sources tab) |
| Stoic quotes (`stoic.tekloon.net`) | Small project. Sometimes has no author |
| Quotable community mirror (`api.quotable.kurokeita.dev`) | The original Quotable API is offline |
| [FavQs](https://favqs.com/) | One quote of the day |

Online quotes come as they are, and their attributions aren't always accurate. Some of these services are small projects and may disappear. A source that fails backs off and is reported on the Sources tab, and the others carry on.

## How it stays light

- It sleeps on one timer between quote changes, with no polling, so idle CPU is about zero.
- It only draws when something changes, and hands unused memory back to Windows afterwards.
- It skips rotation and fetching while the session is locked or the display is off.
- The manager is a separate process that exits when closed.

## Your data

Everything lives in `%APPDATA%\WindowsQuotes\`:

| File | Contents |
|---|---|
| `my-quotes.json` | Your quotes |
| `settings.json` | Your settings |
| `state.json` | Current quote, hold and source status |
| `cache.json` | Saved online quotes |
| `error.log` | Only exists if something went wrong |

## Development

```powershell
dotnet test
```

Useful flags:

| Flag | Effect |
|---|---|
| `--data-dir <path>` | Use a different data folder, so tests don't touch your real quotes |
| `--debug-float` | Float above other windows instead of pinning, to check a look without clearing the desktop |
| `--manager` | Open the manager instead of the widget |

`Widget/DesktopPinner.cs` keeps the window on the desktop layer, and `Widget/Backdrop.cs` applies the frosted effect. Both rely on undocumented Windows behaviour, so they're the first places to check if a Windows update changes how the widget behaves.
