using Subconscious.TUI;
using Subconscious.TUI.Demo;

var screen = new DemoScreen();
new TerminalEventLoop().Run(screen, key => key.Key != ConsoleKey.Q);
