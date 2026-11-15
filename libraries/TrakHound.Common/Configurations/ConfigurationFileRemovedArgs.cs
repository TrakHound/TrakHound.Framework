// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Configurations
{
    struct ConfigurationFileRemovedArgs
    {
        public string Category { get; set; }

        public string Path { get; set; }


        public ConfigurationFileRemovedArgs(string category, string path)
        {
            Category = category;
            Path = path;
        }
    }
}
