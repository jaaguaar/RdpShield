using Bootstrapper.Models;
using Bootstrapper.ViewModels.Util;
using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Bootstrapper.ViewModels
{
  internal class ConfigViewModel : ViewModelBase
  {
    private readonly Model _model;
    private bool _showPrerequisites = true;
    private string _introText = "You are about to install RdpShield on this machine.";
    private string _detailsText = "Prerequisites are required. Without .NET Desktop Runtime and Windows App Runtime, RdpShield Manager will not start.";
    private bool _isDotNetDesktopRuntimeInstalled;
    private bool _isWindowsAppRuntimeInstalled;

    public ConfigViewModel(Model model)
    {
      _model = model;
    }

    public bool InstallDotNetDesktopRuntime
    {
      get
      {
        if (_model.Engine.ContainsVariable("InstallDotNetDesktopRuntime"))
          return _model.Engine.GetVariableNumeric("InstallDotNetDesktopRuntime") == 1;

        return true;
      }
      set
      {
        _model.Engine.SetVariableNumeric("InstallDotNetDesktopRuntime", value ? 1 : 0);
        OnPropertyChanged();
      }
    }

    public bool InstallWindowsAppRuntime
    {
      get
      {
        if (_model.Engine.ContainsVariable("InstallWindowsAppRuntime"))
          return _model.Engine.GetVariableNumeric("InstallWindowsAppRuntime") == 1;

        return true;
      }
      set
      {
        _model.Engine.SetVariableNumeric("InstallWindowsAppRuntime", value ? 1 : 0);
        OnPropertyChanged();
      }
    }

    public void AfterDetect()
    {
      IsDotNetDesktopRuntimeInstalled = DetectDotNetDesktopRuntimeInstalled();
      IsWindowsAppRuntimeInstalled = DetectWindowsAppRuntimeInstalled();

      if (ShowPrerequisites)
      {
        if (IsDotNetDesktopRuntimeInstalled)
          InstallDotNetDesktopRuntime = false;

        if (IsWindowsAppRuntimeInstalled)
          InstallWindowsAppRuntime = false;
      }

      OnPropertyChanged(nameof(InstallDotNetDesktopRuntime));
      OnPropertyChanged(nameof(InstallWindowsAppRuntime));
      OnPropertyChanged(nameof(InstallDesktopShortcut));
      OnPropertyChanged(nameof(InstallStartMenuShortcut));
    }

    public bool IsDotNetDesktopRuntimeInstalled
    {
      get => _isDotNetDesktopRuntimeInstalled;
      private set
      {
        if (_isDotNetDesktopRuntimeInstalled == value)
          return;

        _isDotNetDesktopRuntimeInstalled = value;
        OnPropertyChanged();
      }
    }

    public bool IsWindowsAppRuntimeInstalled
    {
      get => _isWindowsAppRuntimeInstalled;
      private set
      {
        if (_isWindowsAppRuntimeInstalled == value)
          return;

        _isWindowsAppRuntimeInstalled = value;
        OnPropertyChanged();
      }
    }

    public bool InstallDesktopShortcut
    {
      get
      {
        if (_model.Engine.ContainsVariable("InstallDesktopShortcut"))
          return _model.Engine.GetVariableNumeric("InstallDesktopShortcut") == 1;

        return true;
      }
      set
      {
        _model.Engine.SetVariableNumeric("InstallDesktopShortcut", value ? 1 : 0);
        OnPropertyChanged();
      }
    }

    public bool InstallStartMenuShortcut
    {
      get
      {
        if (_model.Engine.ContainsVariable("InstallStartMenuShortcut"))
          return _model.Engine.GetVariableNumeric("InstallStartMenuShortcut") == 1;

        return true;
      }
      set
      {
        _model.Engine.SetVariableNumeric("InstallStartMenuShortcut", value ? 1 : 0);
        OnPropertyChanged();
      }
    }

    public bool ShowPrerequisites
    {
      get => _showPrerequisites;
      private set
      {
        if (_showPrerequisites == value)
          return;

        _showPrerequisites = value;
        OnPropertyChanged();
      }
    }

    public string IntroText
    {
      get => _introText;
      private set
      {
        if (_introText == value)
          return;

        _introText = value;
        OnPropertyChanged();
      }
    }

    public string DetailsText
    {
      get => _detailsText;
      private set
      {
        if (_detailsText == value)
          return;

        _detailsText = value;
        OnPropertyChanged();
      }
    }

    public void SetInstallMode(bool isInstallMode)
    {
      ShowPrerequisites = isInstallMode;

      if (isInstallMode)
      {
        IntroText = "You are about to install RdpShield on this machine.";
        DetailsText = "Prerequisites are required. Without .NET Desktop Runtime and Windows App Runtime, RdpShield Manager will not start.";
      }
      else
      {
        IntroText = "You are about to uninstall RdpShield from this machine.";
        DetailsText = "Prerequisites (.NET Desktop Runtime and Windows App Runtime) are not removed automatically.";
      }
    }

    private bool DetectDotNetDesktopRuntimeInstalled()
    {
      try
      {
        if (_model.Engine.ContainsVariable("DotNetDesktopRuntimeVersion"))
        {
          var version = _model.Engine.GetVariableString("DotNetDesktopRuntimeVersion");
          if (!string.IsNullOrWhiteSpace(version))
          {
            if (_model.Engine.CompareVersions(version, "10.0.0") >= 0)
              return true;

            // Some engines/searches return versions with a leading "v".
            if (_model.Engine.CompareVersions($"v{version.TrimStart('v', 'V')}", "v10.0.0") >= 0)
              return true;
          }
        }
      }
      catch
      {
        // ignore and use fallback checks
      }

      // Fallback: detect by actual dotnet shared runtime folders.
      try
      {
        var sharedDir = Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
          "dotnet",
          "shared",
          "Microsoft.WindowsDesktop.App");

        if (Directory.Exists(sharedDir))
        {
          foreach (var dir in Directory.GetDirectories(sharedDir))
          {
            var name = Path.GetFileName(dir);
            if (string.IsNullOrWhiteSpace(name))
              continue;

            if (name.StartsWith("10.", StringComparison.OrdinalIgnoreCase))
              return true;
          }
        }
      }
      catch
      {
        // ignore
      }

      return false;
    }

    private bool DetectWindowsAppRuntimeInstalled()
    {
      if (!_model.Engine.ContainsVariable("WindowsAppRuntime18Installed"))
      {
        // continue with fallback checks
      }
      else
      {
        try
        {
          if (_model.Engine.GetVariableNumeric("WindowsAppRuntime18Installed") == 1)
            return true;
        }
        catch
        {
          try
          {
            var value = _model.Engine.GetVariableString("WindowsAppRuntime18Installed");
            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase))
              return true;
          }
          catch
          {
            // ignore and use fallback checks
          }
        }
      }

      try
      {
        // Primary registry key used by Burn search
        if (RegistryKeyExistsBothViews(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\Families\Microsoft.WindowsAppRuntime.1.8_8wekyb3d8bbwe"))
          return true;
      }
      catch
      {
        // ignore and continue
      }

      // Fallback: package repository may have package records even if family key was unavailable.
      try
      {
        if (RegistryAnySubKeyStartsWithBothViews(
              @"SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\Applications",
              "Microsoft.WindowsAppRuntime.1.8_"))
          return true;
      }
      catch
      {
        // ignore and continue
      }

      // Fallback: folder probe
      try
      {
        var windowsAppsDir = Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
          "WindowsApps");

        if (Directory.Exists(windowsAppsDir))
        {
          var dirs = Directory.GetDirectories(windowsAppsDir, "Microsoft.WindowsAppRuntime.1.8_*");
          if (dirs.Any())
            return true;
        }
      }
      catch
      {
        // No access to WindowsApps directory on some systems -> ignore.
      }

      return false;
    }

    private static bool RegistryKeyExistsBothViews(string subKeyPath)
    {
      return RegistryKeyExists(RegistryView.Registry64, subKeyPath) ||
             RegistryKeyExists(RegistryView.Registry32, subKeyPath);
    }

    private static bool RegistryKeyExists(RegistryView view, string subKeyPath)
    {
      using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
      using var key = baseKey.OpenSubKey(subKeyPath, false);
      return key != null;
    }

    private static bool RegistryAnySubKeyStartsWithBothViews(string subKeyPath, string prefix)
    {
      return RegistryAnySubKeyStartsWith(RegistryView.Registry64, subKeyPath, prefix) ||
             RegistryAnySubKeyStartsWith(RegistryView.Registry32, subKeyPath, prefix);
    }

    private static bool RegistryAnySubKeyStartsWith(RegistryView view, string subKeyPath, string prefix)
    {
      using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
      using var key = baseKey.OpenSubKey(subKeyPath, false);
      if (key == null)
        return false;

      var names = key.GetSubKeyNames();
      return names.Any(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
  }
}
