using System.Collections.ObjectModel;
using System.Collections.Generic;

namespace ColorClickApp
{
    public class AppConfig
    {
        public ObservableCollection<TaskInfo> Tasks { get; set; }

        public AppConfig()
        {
            Tasks = new ObservableCollection<TaskInfo>();
        }
    }
}