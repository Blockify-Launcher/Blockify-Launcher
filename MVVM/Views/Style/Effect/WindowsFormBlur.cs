using BlockifyLauncher.MVVM.Views.Style.Effect.Native;

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BlockifyLauncher.MVVM.Views.Style.Effect
{
    public class WindowFormBlur
    {
        private Window _window;

        public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
            "IsEnable", typeof(bool), typeof(WindowFormBlur), new PropertyMetadata(false, OnIsEnabledChanged));

        public static void SetIsEnabled(DependencyObject element, bool val) =>
            element.SetValue(IsEnabledProperty, val);

        public static void SetWindowBlur(DependencyObject element, WindowFormBlur val) =>
            element.SetValue(WindowBlurProperty, val);

        public static readonly DependencyProperty WindowBlurProperty = DependencyProperty.RegisterAttached("WindowFormBlur",
            typeof(WindowFormBlur), typeof(WindowFormBlur), new PropertyMetadata(null, OnWindowBlurChanged));

        public static bool GetIsEnabled(DependencyObject element)
        { return element.GetValue(IsEnabledProperty) is bool; }

        public static WindowFormBlur GetWindowBlur(DependencyObject element)
        { return element.GetValue(WindowBlurProperty) as WindowFormBlur; }

        public static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Window window)
            {
                if (true.Equals(e.OldValue))
                {
                    GetWindowBlur(window)?.Detach();
                    window.ClearValue(WindowBlurProperty);
                }
                if (true.Equals(e.NewValue))
                {
                    WindowFormBlur blur = new WindowFormBlur();
                    blur.Attach(window);
                    window.SetValue(WindowBlurProperty, blur);
                }
            }
        }

        [DllImport("user32.dll")]
        internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        private static void EnableBlur(Window window)
        {
            WindowInteropHelper windowHelper = new WindowInteropHelper(window);
            AccentPolicy accent = new AccentPolicy { AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND };
            int accentStructSize = Marshal.SizeOf(accent);
            IntPtr accentPtr = Marshal.AllocHGlobal(accentStructSize);
            Marshal.StructureToPtr(accent, accentPtr, false);

            WindowCompositionAttributeData data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                SizeOfData = accentStructSize,
                Data = accentPtr
            };

            SetWindowCompositionAttribute(windowHelper.Handle, ref data);
            Marshal.FreeHGlobal(accentPtr);
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            ((Window)sender).SourceInitialized -= OnSourceInitialized;
            AttachCore();
        }

        private static void OnWindowBlurChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Window window)
            {
                (e.OldValue as WindowFormBlur)?.Detach();
                (e.NewValue as WindowFormBlur)?.Attach(window);
            }
        }

        private void AttachCore() =>
            EnableBlur(_window);

        private void Attach(Window window)
        {
            _window = window;
            HwndSource source = PresentationSource.FromVisual(window) as HwndSource;
            if (source == null)
                window.SourceInitialized += OnSourceInitialized;
            else
                AttachCore();
        }

        private void Detach()
        {
            try
            {
                DetachCore();
            }
            finally
            {
                _window = null;
            }
        }

        private void DetachCore() =>
            _window.SourceInitialized += OnSourceInitialized;


    }

    #region Native block.
    namespace Native
    {
        internal enum AccentState
        {
            ACCENT_DISABLED,
            ACCENT_ENABLE_GRADIENT,
            ACCENT_ENABLE_TRANSPARENTGRADIENT,
            ACCENT_ENABLE_BLURBEHIND,
            ACCENT_INVALID_STATE
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        internal enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19
        }
    }
    #endregion
}
