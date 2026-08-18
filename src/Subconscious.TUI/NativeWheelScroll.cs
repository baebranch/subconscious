using System.Runtime.InteropServices;

namespace Subconscious.TUI;

/// <summary>Normalizes native wheel deltas using the host OS line/page preference.</summary>
internal sealed class NativeWheelScroll
{
    private const int WheelDelta = 120;
    private readonly WheelScrollPreference _preference = WheelScrollPreference.Read();
    private int _remainder;

    public bool TryNormalize(int nativeDelta, out TerminalScrollEvent scroll)
    {
        _remainder += nativeDelta;
        var notches = _remainder / WheelDelta;
        _remainder %= WheelDelta;
        if (notches == 0)
        {
            scroll = default;
            return false;
        }

        if (_preference.IsPageScroll)
        {
            scroll = new TerminalScrollEvent(-notches, IsPageScroll: true);
            return true;
        }

        var lineDelta = -(long)notches * _preference.Lines;
        scroll = new TerminalScrollEvent((int)Math.Clamp(lineDelta, int.MinValue, int.MaxValue), IsPageScroll: false);
        return scroll.Delta != 0;
    }

    private readonly record struct WheelScrollPreference(int Lines, bool IsPageScroll)
    {
        private const uint GetWheelScrollLines = 0x0068;
        private const uint WheelPageScroll = uint.MaxValue;

        public static WheelScrollPreference Read()
        {
            if (!OperatingSystem.IsWindows()
                || !SystemParametersInfoW(GetWheelScrollLines, 0, out var configuredLines, 0))
            {
                return new WheelScrollPreference(1, IsPageScroll: false);
            }

            return configuredLines == WheelPageScroll
                ? new WheelScrollPreference(0, IsPageScroll: true)
                : new WheelScrollPreference((int)Math.Min(configuredLines, int.MaxValue), IsPageScroll: false);
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfoW(uint action, uint parameter, out uint value, uint update);
    }
}
