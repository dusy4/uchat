using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace uchat_server.Models
{
    public class GroupDetailsModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int CreatorId { get; set; }
        public string? AvatarData { get; set; }
        public List<UserItemModel> Members { get; set; } = new();
    }
}
