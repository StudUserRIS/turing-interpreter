namespace Интерпретатор_машины_Тьюринга
{
    public class Group
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int Version { get; set; }
        public override string ToString() => Name;
    }
}
