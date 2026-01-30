using System.Collections.ObjectModel;

namespace WINHOME
{
    public class TileGroupView
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "常用";
        public int Order { get; set; }
        public ObservableCollection<AppInfo> Items { get; set; } = new();
    }
}
