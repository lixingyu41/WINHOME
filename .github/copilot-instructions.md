# Copilot Instructions

## General Guidelines
- Use P/Invoke where necessary.
- Ensure the application prioritizes stability and startup speed.
- Keep the window open when hidden, without exiting.

## Key Bindings
- Use Alt+Space as the default hotkey to pop up the top-level borderless window.
- Do not intercept the single Win key.

## Configuration
- Store configuration files at `%AppData%\MyLauncher\config.json`.

## Project-Specific Rules
- Target WPF/.NET 8/x64.
- Implement a simple ViewModel using `INotifyPropertyChanged`.
- Use `UseShellExecute=true` to launch targets.