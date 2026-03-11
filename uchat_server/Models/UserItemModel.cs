using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace uchat_server.Models
{
    public class UserItemModel 
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string AvatarColor { get; set; } = "#0088CC";
        public string? AvatarData { get; set; }
        public int GroupId { get; internal set; }
        public bool IsGroup { get; internal set; }
        public string Initials { get; internal set; }
        public int CreatorId { get; set; }
        public bool IsGroupOwner { get; set; }
        public bool IsDeleted { get; set; }
        public string ExitMenuText
        {
            get
            {
                if (!IsGroup) return "Delete Chat";
                return IsGroupOwner ? "Delete Group" : "Leave Group";
            }
        }
    }
}