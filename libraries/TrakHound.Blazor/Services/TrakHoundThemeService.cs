// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Radzen;
using System.Collections.Concurrent;
using TrakHound.Apps;

namespace TrakHound.Blazor.Services
{
    public class TrakHoundThemeService
    {
        private static readonly ConcurrentDictionary<string, TrakHoundThemes> _defaultThemes = new ConcurrentDictionary<string, TrakHoundThemes>(); // Key => Theme
        private readonly ITrakHoundAppLocalStorageService _localStorage;
        private TrakHoundThemes _theme;
        private bool _themeSet;


        public TrakHoundThemes Theme => _theme;

        /// <summary>
        /// Whether the Theme has been read from storage (Load). Before then, the Key's default Theme is shown
        /// </summary>
        public bool IsLoaded => _themeSet;


        public event EventHandler<TrakHoundThemes> ThemeChanged;


        public TrakHoundThemeService(ITrakHoundAppLocalStorageService localStorage)
        {
            _localStorage = localStorage;
        }

        public async Task Load(string key)
        {
            if (!_themeSet && !string.IsNullOrEmpty(key))
            {
                _themeSet = true;

                var value = await _localStorage.GetItem<string>(key);
                if (value != null)
                {
                    _theme = value.ConvertEnum<TrakHoundThemes>();

                    if (ThemeChanged != null) ThemeChanged.Invoke(this, _theme);
                }
                else
                {
                    _theme = GetDefaultTheme(key);

                    if (ThemeChanged != null) ThemeChanged.Invoke(this, _theme);
                }
            }
        }

        public async Task SetTheme(string key, TrakHoundThemes theme)
        {
            if (_theme != theme && !string.IsNullOrEmpty(key))
            {
                _theme = theme;

                await _localStorage.SetItem<string>(key, theme.ToString());

                if (ThemeChanged != null) ThemeChanged.Invoke(this, theme);
            }
        }


        public static TrakHoundThemes GetDefaultTheme(string key)
        {
            if (!string.IsNullOrEmpty(key))
            {
                return _defaultThemes.GetValueOrDefault(key);
            }

            return default;
        }

        public static void SetDefaultTheme(string key, TrakHoundThemes theme)
        {
            if (!string.IsNullOrEmpty(key))
            {
                _defaultThemes[key] = theme;
            }
        }

        /// <summary>
        /// The Key that an App's Theme is stored with on an Instance
        /// </summary>
        public static string GetAppKey(string appId) => $"trakhound.app.theme.{appId}";
    }
}
