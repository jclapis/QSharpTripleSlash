/* ========================================================================
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *     http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 * ======================================================================== */

using Nett;
using System;
using System.IO;

namespace QSharpTripleSlash.Common
{
    /// <summary>
    /// This class manages QSharpTripleSlash's configuration settings.
    /// </summary>
    public class ConfigManager
    {
        /// <summary>
        /// The settings that were parsed from the config file
        /// </summary>
        private readonly TomlTable ConfigSettings;


        /// <summary>
        /// Creates a new ConfigManager, and attempts to load the config file from the given directory.
        /// </summary>
        /// <param name="ConfigFilePath">The directory containing the config file</param>
        /// <exception cref="Exception">Throws an exception if loading fails.</exception>
        public ConfigManager(string ConfigFilePath)
        {
            string configFile = Path.Combine(ConfigFilePath, "config.toml");
            if (File.Exists(configFile))
            {
                ConfigSettings = Toml.ReadFile(configFile);
            }
            else
            {
                throw new FileNotFoundException("Config file doesn't exist.", configFile);
            }
        }


        /// <summary>
        /// Retrieves a configuration setting.
        /// </summary>
        /// <param name="SectionName">The section name that the setting belongs to</param>
        /// <param name="SettingName">The name of the setting</param>
        /// <param name="Setting">The value of the setting, in string form</param>
        /// <returns>True if the setting was found, false if it wasn't.</returns>
        public bool GetConfigSetting(string SectionName, string SettingName, out string Setting)
        {
            if(ConfigSettings.TryGetValue(SectionName, out TomlObject sectionObject))
            {
                TomlTable section = (TomlTable)sectionObject;
                if(section.TryGetValue(SettingName, out TomlObject settingObject))
                {
                    Setting = settingObject.ToString();
                    return true;
                }
            }

            Setting = null;
            return false;
        }

    }
}
