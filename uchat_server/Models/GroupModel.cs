namespace uchat_server.Models
{
    public class GroupModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int CreatorId { get; set; }
    }
}