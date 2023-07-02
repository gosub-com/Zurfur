using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;

namespace Zurfur.Ide
{
    public class ZurfProject
    {
        public ZurfProjectData Data { get; set; } = new ZurfProjectData();

        public string ProjectDirectory => Path.GetDirectoryName(Data.ProjectPath);
        public string ProjectName => Path.GetDirectoryName(Data.ProjectPath);

        /// <summary>
        /// Load configuration file, throw exception if it's invalid.
        /// Sets ProjectPath after loading the file.
        /// </summary>
        public static ZurfProject Load(string fileName)
        {
            var project = new ZurfProject();
            project.LoadInternal(fileName);
            return project;
        }

        void LoadInternal(string fileName)
        {
            var config = JsonSerializer.Deserialize<ZurfProjectData>(File.ReadAllText(fileName));
            if (config == null || !config.IsValid())
                throw new Exception("Invalid configuration file.");
            config.ProjectPath = fileName;
            Data = config;
        }

        /// <summary>
        /// Save the configuration file.  
        /// Sets Zurfur, Version, and ProjectPath before saving.
        /// </summary>
        public void Save(string fileName)
        {
            Data.Zurfur = ZurfProjectData.ZURFUR;
            Data.Version = ZurfProjectData.VERSION;
            Data.ProjectPath = fileName;
            File.WriteAllText(fileName, JsonSerializer.Serialize(Data));
        }

    }

}
