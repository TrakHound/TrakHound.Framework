// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Configurations;

namespace TrakHound.Instance.Zones.Tests
{
    /// <summary>
    /// A Configuration Profile kept in memory (nothing is read from or written to disk)
    /// </summary>
    internal class InMemoryConfigurationProfile : ITrakHoundConfigurationProfile
    {
        private readonly List<ITrakHoundConfiguration> _configurations = new List<ITrakHoundConfiguration>();

        public IEnumerable<ITrakHoundConfiguration> Configurations => _configurations.ToList();

        public string Id => "test";

        public string Path => null;

        public string Name { get; set; }

        public string Version { get; set; }

        public bool Enabled { get; set; } = true;

        public int SaveCount { get; private set; }


        public event EventHandler<ITrakHoundConfiguration> ConfigurationAdded;

        public event EventHandler<ITrakHoundConfiguration> ConfigurationRemoved;

        public event EventHandler<TrakHoundConfigurationChangedArgs> ConfigurationChanged;


        public IEnumerable<TConfiguration> Get<TConfiguration>(string category) where TConfiguration : ITrakHoundConfiguration
        {
            return _configurations.Where(o => o.Category == category).OfType<TConfiguration>().ToList();
        }

        public TConfiguration Get<TConfiguration>(string category, string configurationId) where TConfiguration : ITrakHoundConfiguration
        {
            return Get<TConfiguration>(category).FirstOrDefault(o => o.Id == configurationId);
        }

        public object Get(string category, string configurationId)
        {
            return _configurations.FirstOrDefault(o => o.Category == category && o.Id == configurationId);
        }

        public void Add(ITrakHoundConfiguration configuration, bool save = false)
        {
            var existing = _configurations.RemoveAll(o => o.Category == configuration.Category && o.Id == configuration.Id) > 0;
            _configurations.Add(configuration);
            if (save) SaveCount++;
            ConfigurationAdded?.Invoke(this, configuration);
            ConfigurationChanged?.Invoke(this, new TrakHoundConfigurationChangedArgs(existing ? TrakHoundConfigurationChangeType.Updated : TrakHoundConfigurationChangeType.Added, configuration));
        }

        public void Add(string category, string configurationPath) => throw new NotSupportedException();

        public void Remove(string category, string configurationId)
        {
            var removed = _configurations.Where(o => o.Category == category && o.Id == configurationId).ToList();
            foreach (var configuration in removed)
            {
                _configurations.Remove(configuration);
                ConfigurationRemoved?.Invoke(this, configuration);
                ConfigurationChanged?.Invoke(this, new TrakHoundConfigurationChangedArgs(TrakHoundConfigurationChangeType.Removed, configuration));
            }
        }

        public void Clear(string category)
        {
            foreach (var configuration in _configurations.Where(o => o.Category == category).ToList()) Remove(category, configuration.Id);
        }

        public IEnumerable<TConfiguration> Read<TConfiguration>(string category) where TConfiguration : ITrakHoundConfiguration => Get<TConfiguration>(category);

        public TConfiguration Read<TConfiguration>(string category, string configurationId) where TConfiguration : ITrakHoundConfiguration => Get<TConfiguration>(category, configurationId);

        public void Save(string category, string configurationId) => SaveCount++;

        public void Delete(string category, string configurationId) { }

        public void Load<TConfiguration>(string category) where TConfiguration : ITrakHoundConfiguration { }

        public void Dispose() { }
    }
}
