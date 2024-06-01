namespace RitualWorks.Db
{
    public class RitualType
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public ICollection<Ritual>? Rituals { get; set; }
    }

}