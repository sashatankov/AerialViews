using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace AerialViews.UI
{
    public partial class VideoOverlay : UserControl
    {
        private int _animationGeneration;

        public VideoOverlay()
        {
            InitializeComponent();
        }

        public void SetBlack()
        {
            _animationGeneration++;
            FadeOverlay.BeginAnimation(UIElement.OpacityProperty, null);
            FadeOverlay.Opacity = 1;
        }

        public void FadeFromBlack(int durationMs, Action? onCompleted = null)
        {
            AnimateFade(0, durationMs, onCompleted);
        }

        public void FadeToBlack(int durationMs, Action? onCompleted = null)
        {
            AnimateFade(1, durationMs, onCompleted);
        }

        private void AnimateFade(double targetOpacity, int durationMs, Action? onCompleted)
        {
            var generation = ++_animationGeneration;
            var startOpacity = FadeOverlay.Opacity;

            FadeOverlay.BeginAnimation(UIElement.OpacityProperty, null);
            FadeOverlay.Opacity = startOpacity;

            if (durationMs <= 0 || Math.Abs(startOpacity - targetOpacity) < 0.001)
            {
                FadeOverlay.Opacity = targetOpacity;
                onCompleted?.Invoke();
                return;
            }

            var animation = new DoubleAnimation(
                startOpacity,
                targetOpacity,
                TimeSpan.FromMilliseconds(durationMs))
            {
                FillBehavior = FillBehavior.HoldEnd
            };

            animation.Completed += (s, e) =>
            {
                if (generation != _animationGeneration) return;

                FadeOverlay.BeginAnimation(UIElement.OpacityProperty, null);
                FadeOverlay.Opacity = targetOpacity;
                onCompleted?.Invoke();
            };

            FadeOverlay.BeginAnimation(
                UIElement.OpacityProperty,
                animation,
                HandoffBehavior.SnapshotAndReplace);
        }
    }
}
