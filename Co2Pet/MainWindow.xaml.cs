using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using WpfPath = System.Windows.Shapes.Path;

namespace Co2Pet
{
    public partial class MainWindow : Window
    {
        private enum PetMood
        {
            Happy,
            Mid,
            Sad
        }

        private DispatcherTimer timer;
        private DispatcherTimer priceTimer;
        private HttpClient client = new HttpClient();
        private readonly Forms.NotifyIcon trayIcon;
        private int _lastAlertedHour = -1;
        private string _lastMood = "";
        private bool _manualAccessoryOverride;
        private int _clickCount;
        private DateTime _lastClickTime;
        private bool _soundEnabled = true;
        private DialogService dialogService;
        private readonly Random _random = new();
        private readonly string[] _pettingMessages =
        {
            "Heehee! That tickles!",
            "Aww, you're sweet!",
            "Dino hugs for you!",
            "That was a lovely pet!",
            "Rawr! Best friends!"
        };
        private double? _latestCo2;
        private double? _latestTemperature;
        private double? _latestWindPercentage;
        private double _latestWindSpeedRatio = 1.0;

        public MainWindow()
        {
            InitializeComponent();
            dialogService = new DialogService(DialogText, DialogBox, () => _soundEnabled);

            var trayMenu = new Forms.ContextMenuStrip();
            trayMenu.Items.Add("Show/Hide Pet", null, (_, _) =>
            {
                Visibility = Visibility == Visibility.Visible
                    ? Visibility.Hidden
                    : Visibility.Visible;
            });
            trayMenu.Items.Add("Exit", null, (_, _) => System.Windows.Application.Current.Shutdown());

            trayIcon = new Forms.NotifyIcon
            {
                Icon = GetDynamicTrayIcon(),
                Text = "Grid Pet",
                Visible = true,
                ContextMenuStrip = trayMenu
            };

            _ = UpdateGridData();
            _ = UpdateWeatherAsync();
            _ = UpdateWindSpeedAsync();
            UpdateTimeOfDayBackground();

            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMinutes(5);
            timer.Tick += (_, _) => _ = UpdateGridData();
            timer.Start();

            priceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(10)
            };
            priceTimer.Tick += async (_, _) =>
            {
                await CheckCheapestHourAsync();
                await UpdateWeatherAsync();
                await UpdateWindSpeedAsync();
                UpdateTimeOfDayBackground();
            };
            priceTimer.Start();
            _ = CheckCheapestHourAsync();

            var timeOfDayTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(1)
            };
            timeOfDayTimer.Tick += (_, _) => UpdateTimeOfDayBackground();
            timeOfDayTimer.Start();
        }

        private static System.Drawing.Icon GetDynamicTrayIcon()
        {
            var petPath = new WpfPath
            {
                Data = Geometry.Parse("M 5 70 Q 25 65 35 55 Q 50 45 68 45 Q 76 45 80 25 Q 80 5 90 5 L 95 5 Q 98 5 98 10 L 90 12 Q 85 25 85 50 L 85 82 A 7 7 0 0 1 71 82 L 71 72 Q 58 70 45 72 L 45 82 A 7 7 0 0 1 31 82 L 31 70 Q 15 75 5 70 Z"),
                Fill = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#7ED957")),
                Stretch = System.Windows.Media.Stretch.Uniform,
                Width = 32,
                Height = 32
            };

            petPath.Measure(new System.Windows.Size(32, 32));
            petPath.Arrange(new System.Windows.Rect(0, 0, 32, 32));
            petPath.UpdateLayout();

            var bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(petPath);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var stream = new MemoryStream();
            encoder.Save(stream);
            stream.Position = 0;

            using var drawingBitmap = new System.Drawing.Bitmap(stream);
            using var sourceIcon = System.Drawing.Icon.FromHandle(drawingBitmap.GetHicon());
            return (System.Drawing.Icon)sourceIcon.Clone();
        }

        private async Task UpdateGridData()
        {
            try
            {
                string url = "https://api.energidataservice.dk/dataset/CO2Emis?limit=1&filter={\"PriceArea\":\"DK1\"}&sort=Minutes5DK%20Desc";
                string response = await client.GetStringAsync(url);

                using (JsonDocument doc = JsonDocument.Parse(response))
                {
                    JsonElement root = doc.RootElement;
                    double co2 = root.GetProperty("records")[0].GetProperty("CO2Emission").GetDouble();

                    _latestCo2 = co2;
                    CO2Text.Text = $"CO2: {Math.Round(co2, 1)} g/kWh";
                    UpdateInfoPanel();

                    PetMood mood = co2 < 60
                        ? PetMood.Happy
                        : co2 < 120
                            ? PetMood.Mid
                            : PetMood.Sad;
                    ApplyPetMood(mood);
                }
            }
            catch
            {
                _ = dialogService.ShowAsync("Connection Error", 4000);
            }
        }

        private async Task UpdateWindSpeedAsync()
        {
            try
            {
                const string url =
                    "https://api.energidataservice.dk/dataset/PowerSystemRightNow?limit=1";
                string response = await client.GetStringAsync(url);

                using JsonDocument doc = JsonDocument.Parse(response);
                JsonElement record = doc.RootElement.GetProperty("records")[0];
                double windPower = record.GetProperty("OnshoreWindPower").GetDouble()
                    + record.GetProperty("OffshoreWindPower").GetDouble();
                double totalGeneration = record.GetProperty("ProductionGe100MW").GetDouble()
                    + record.GetProperty("ProductionLt100MW").GetDouble();

                if (totalGeneration <= 0)
                {
                    System.Diagnostics.Debug.WriteLine("Wind update skipped because total generation is not positive.");
                    return;
                }

                double windPercentage = windPower / totalGeneration * 100;
                _latestWindPercentage = windPercentage;
                _latestWindSpeedRatio = windPercentage < 10
                    ? 0.5
                    : windPercentage > 50
                        ? 2.0
                        : 1.0;
                PetRockAnimation.SpeedRatio = _latestWindSpeedRatio;
                UpdateInfoPanel();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Wind update failed: {ex.Message}");
            }
        }

        private void UpdateTimeOfDayBackground()
        {
            int hour = DateTime.Now.Hour;
            string background = hour switch
            {
                >= 6 and < 9 => "#FFFFCC",
                >= 9 and < 18 => "#D6F0FF",
                >= 18 and < 21 => "#FFD1A9",
                _ => "#B8C7E0"
            };

            PetPanel.Background = new SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(background));
        }

        private async Task UpdateWeatherAsync()
        {
            try
            {
                const string url =
                    "https://api.open-meteo.com/v1/forecast?latitude=55.47&longitude=8.45&current_weather=true";
                string response = await client.GetStringAsync(url);

                using JsonDocument doc = JsonDocument.Parse(response);
                double temperature = doc.RootElement
                    .GetProperty("current_weather")
                    .GetProperty("temperature")
                    .GetDouble();

                _latestTemperature = temperature;
                TemperatureText.Text = $"Temp: {temperature:0.#}°C";
                UpdateInfoPanel();

                if (!_manualAccessoryOverride)
                {
                    ApplyAccessoryVisibility(
                        temperature < 10,
                        temperature >= 10 && temperature <= 18,
                        temperature > 18);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Weather update failed: {ex.Message}");
            }
        }

        private void AccessorySelector_SelectionChanged(object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (AccessorySelector.SelectedIndex < 0)
            {
                return;
            }

            _manualAccessoryOverride = true;
            ApplyAccessoryVisibility(
                AccessorySelector.SelectedIndex == 0,
                AccessorySelector.SelectedIndex == 1,
                AccessorySelector.SelectedIndex == 2);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsPanel.Visibility = SettingsPanel.Visibility == Visibility.Visible
                ? Visibility.Hidden
                : Visibility.Visible;
            e.Handled = true;
        }

        private void InfoButton_Click(object sender, RoutedEventArgs e)
        {
            InfoPanel.Visibility = InfoPanel.Visibility == Visibility.Visible
                ? Visibility.Hidden
                : Visibility.Visible;
            UpdateInfoPanel();
            if (InfoPanel.Visibility == Visibility.Visible)
            {
                _ = dialogService.ShowAsync(
                    dialogService.BuildDataComment(_latestCo2, _latestTemperature, _latestWindPercentage),
                    4000);
            }

            e.Handled = true;
        }

        private void UpdateInfoPanel()
        {
            InfoWindText.Text = _latestWindPercentage.HasValue
                ? $"Wind: {_latestWindPercentage.Value:0.0}% ({_latestWindSpeedRatio:0.0}x)"
                : "Wind: Unavailable";
        }

        private void SoundToggle_Changed(object sender, RoutedEventArgs e)
        {
            _soundEnabled = SoundToggle.IsChecked == true;
        }

        private async void RefreshDataButton_Click(object sender, RoutedEventArgs e)
        {
            await UpdateGridData();
            await CheckCheapestHourAsync();
            await UpdateWeatherAsync();
            await UpdateWindSpeedAsync();
            UpdateTimeOfDayBackground();
            await dialogService.ShowAsync(
                $"Data refreshed! {dialogService.BuildDataComment(_latestCo2, _latestTemperature, _latestWindPercentage)}",
                4000);
        }

        private void ApplyAccessoryVisibility(bool showBeanie, bool showTopHat, bool showSunHat)
        {
            BeanieHatContainer.Visibility = showBeanie ? Visibility.Visible : Visibility.Hidden;
            BeanieHat.Visibility = showBeanie ? Visibility.Visible : Visibility.Hidden;
            TopHat.Visibility = showTopHat ? Visibility.Visible : Visibility.Hidden;
            SunHat.Visibility = showSunHat ? Visibility.Visible : Visibility.Hidden;
        }

        private void ApplyPetMood(PetMood mood)
        {
            string message;
            string mouthGeometry;
            string bodyColor;

            switch (mood)
            {
                case PetMood.Happy:
                    bodyColor = "#7ED957";
                    mouthGeometry = "M 0 0 Q 4 2 8 0";
                    message = "Happy Dino!";
                    break;
                case PetMood.Mid:
                    bodyColor = "#A8D5BA";
                    mouthGeometry = "M 0 1 L 8 1";
                    message = "Dino is okay.";
                    break;
                default:
                    bodyColor = "#8FAF8F";
                    mouthGeometry = "M 0 2 Q 4 0 8 2";
                    message = "Sad Dino...";
                    break;
            }

            PetPath.Fill = new SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(bodyColor));
            MouthPath.Data = Geometry.Parse(mouthGeometry);

            if (message != _lastMood)
            {
                _lastMood = message;
                _ = dialogService.ShowAsync(message, 4000);
            }
        }

        private async Task CheckCheapestHourAsync()
        {
            try
            {
                string today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                string filter = Uri.EscapeDataString("{\"PriceArea\":\"DK1\"}");
                string url = $"https://api.energidataservice.dk/dataset/Elspotprices?limit=1000&filter={filter}&sort=HourDK%20Desc";
                string response = await client.GetStringAsync(url);

                using JsonDocument doc = JsonDocument.Parse(response);
                JsonElement records = doc.RootElement.GetProperty("records");
                JsonElement? cheapestRecord = null;
                double cheapestPrice = double.MaxValue;

                foreach (JsonElement record in records.EnumerateArray())
                {
                    if (!record.TryGetProperty("HourDK", out JsonElement hourElement) ||
                        hourElement.ValueKind != JsonValueKind.String ||
                        !record.TryGetProperty("SpotPriceDKK", out JsonElement priceElement))
                    {
                        continue;
                    }

                    string? timeString = hourElement.GetString();
                    if (string.IsNullOrWhiteSpace(timeString))
                    {
                        continue;
                    }

                    DateTime hour;
                    try
                    {
                        hour = DateTime.Parse(timeString, CultureInfo.InvariantCulture);
                    }
                    catch (FormatException)
                    {
                        continue;
                    }

                    if (hour.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) != today)
                    {
                        continue;
                    }

                    double price = priceElement.ValueKind == JsonValueKind.Number
                        ? priceElement.GetDouble()
                        : double.Parse(priceElement.GetString() ?? string.Empty,
                            CultureInfo.InvariantCulture);

                    if (price < cheapestPrice)
                    {
                        cheapestPrice = price;
                        cheapestRecord = record;
                    }

                }

                UpdateInfoPanel();

                if (cheapestRecord.HasValue &&
                    cheapestRecord.Value.GetProperty("HourDK").ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(
                        cheapestRecord.Value.GetProperty("HourDK").GetString(),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeLocal,
                        out DateTime cheapestHour) &&
                    cheapestHour.Hour == DateTime.Now.Hour)
                {
                    int currentHour = DateTime.Now.Hour;
                    if (currentHour != _lastAlertedHour)
                    {
                        _lastAlertedHour = currentHour;
                        _ = dialogService.ShowAsync(
                            "Best time for chores! Electricity is at its cheapest right now.",
                            10000);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Price check failed: {ex.Message}");
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void DinoContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DateTime now = DateTime.Now;
            _clickCount = now - _lastClickTime < TimeSpan.FromMilliseconds(500)
                ? _clickCount + 1
                : 1;
            _lastClickTime = now;

            DragMove();

            if (_clickCount == 3)
            {
                _clickCount = 0;
                if (_soundEnabled)
                {
                    System.Media.SystemSounds.Asterisk.Play();
                }

                string message = _pettingMessages[_random.Next(_pettingMessages.Length)];
                _ = dialogService.ShowAsync(message, 3000);
            }

            e.Handled = true;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            timer.Stop();
            priceTimer.Stop();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            base.OnClosed(e);
        }
    }
}