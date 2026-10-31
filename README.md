# Co2Pet

![Co2Pet screenshot](/img.png)

Co2Pet is a small WPF desktop companion that turns live energy and weather data into an animated dinosaur pet. The widget is borderless, transparent, always on top, draggable, and available from the Windows system tray.

## Features

- Changes the dinosaur's mood using live DK1 CO2 emissions:
  - Below 60 g/kWh: happy
  - 60-119.9 g/kWh: okay
  - 120 g/kWh or higher: sad
- Adjusts the rocking animation speed using live wind generation.
- Shows weather-based accessories:
  - Beanie below 10°C
  - Top hat from 10°C through 18°C
  - Sun hat above 18°C
- Provides settings for manually testing accessories and enabling or muting sound effects.
- Includes a live-data panel for CO2, temperature, wind percentage, and animation speed.
- Supports manual data refresh and time-based background colors.
- Shows cheapest-electricity-hour alerts.
- Supports triple-click petting with randomized cute responses.
- Includes a typewriter-style dialogue box and optional built-in Windows sounds.
- Provides system tray actions for showing or hiding the pet and exiting the application.

## Data sources

The application reads live data from:

- [Energi Data Service](https://api.energidataservice.dk/) for DK1 CO2, wind, and electricity data.
- [Open-Meteo](https://open-meteo.com/) for current temperature at latitude `55.47`, longitude `8.45`.

Network failures are logged through `Debug.WriteLine`; the application keeps the last valid display state where possible.

## Requirements

- Windows
- .NET SDK 9.0 or a compatible later minor SDK

The project uses WPF and Windows Forms for the system tray icon. It does not require external NuGet packages.

## Running

From the repository root:

```powershell
dotnet run --project .\Co2Pet\Co2Pet.csproj
```

## Building

```powershell
dotnet build .\Co2Pet\Co2Pet.csproj
```

The application targets `net9.0-windows` and is built as a Windows executable.

## Using the widget

- Drag the dinosaur to move it around the desktop.
- Use the `ⓘ` button to view the latest cached data.
- Use the `⚙` button to open settings.
- Select an accessory to manually test it.
- Select **Refresh data** to update all live data immediately.
- Triple-click the dinosaur to trigger a petting reaction.
- Use the tray icon to show or hide the pet or exit the application.
