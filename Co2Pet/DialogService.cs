using System.Windows;
using System.Windows.Controls;

namespace Co2Pet
{
    public sealed class DialogService
    {
        private readonly TextBlock dialogText;
        private readonly Border dialogBox;
        private readonly Func<bool> isSoundEnabled;
        private int dialogVersion;
        private readonly Random _random = new Random();

        public DialogService(TextBlock dialogText, Border dialogBox, Func<bool> isSoundEnabled)
        {
            this.dialogText = dialogText;
            this.dialogBox = dialogBox;
            this.isSoundEnabled = isSoundEnabled;
        }

        public async Task ShowAsync(string text, int durationMs)
        {
            int currentVersion = Interlocked.Increment(ref dialogVersion);
            dialogText.Text = text;
            dialogBox.Visibility = Visibility.Visible;

            for (int index = 0; index < text.Length; index++)
            {
                if (currentVersion != Volatile.Read(ref dialogVersion))
                {
                    return;
                }

                dialogText.Text = text[..(index + 1)];
                if (isSoundEnabled() && (index + 1) % 3 == 0)
                {
                    System.Media.SystemSounds.Beep.Play();
                }

                await Task.Delay(30);
            }

            await Task.Delay(durationMs);
            if (currentVersion == Volatile.Read(ref dialogVersion))
            {
                dialogBox.Visibility = Visibility.Hidden;
            }
        }

        public string BuildDataComment(
            double? co2,
            double? temperature,
            double? windPercentage,
            bool isRefresh = false)
        {
            var comments = new List<string>();

            if (co2.HasValue)
            {
                comments.Add(co2.Value < 60
                    ? "Deliciously green energy right now! Yum!"
                    : co2.Value < 120
                        ? "The grid is looking perfectly average."
                        : "Oof, the grid is pretty dirty right now.");
            }

            if (temperature.HasValue)
            {
                comments.Add(temperature.Value < 10
                    ? "Brrr! Glad I have my winter beanie."
                    : temperature.Value > 18
                        ? "Sun is out, shades on."
                        : "Mild and pleasant. A perfect day for a top hat.");
            }

            if (windPercentage.HasValue)
            {
                comments.Add(windPercentage.Value > 50
                    ? "The turbines are spinning fast out there!"
                    : windPercentage.Value < 10
                        ? "The wind turbines are taking a nap today."
                        : "Just a gentle breeze keeping things moving.");
            }

            if (comments.Count == 0)
            {
                return "I am still gathering today's data.";
            }

            // Pick one random fact to keep the text short
            string selectedComment = comments[_random.Next(comments.Count)];
            
            return isRefresh ? $"Data refreshed! {selectedComment}" : selectedComment;
        }
    }
}