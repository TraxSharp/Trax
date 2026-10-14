// What the compiled examples on effect/effect-providers/domain-data-contexts.md take as given.
public class Book
{
    public int Id { get; set; }
    public BookFormat Format { get; set; }
}

public class Author
{
    public int Id { get; set; }
}

public enum BookFormat
{
    Hardcover,
    Paperback,
}
