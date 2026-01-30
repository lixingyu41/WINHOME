using System.Collections.ObjectModel;

namespace WINHOME
{
    public class TileGroupView
    {
        public string Name { get; set; } = "常用";
        public ObservableCollection<AppInfo> Items { get; set; } = new();
    }
}
