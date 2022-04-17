namespace WebReaper.Domain;

public class WebEl
{

    public WebEl[]? Children { get; set; }

    public string Field { get; set; }

    public string? Selector { get; set; }

    public JsonType? Type { get; set; }

    public WebEl(
        string field,
        string selector,
        JsonType type = JsonType.String,
        string[]? excludeSelectors = null)
    {
        Field = field;
        Selector = selector;
        Type = type;
    }

    public WebEl(
        string field,
        params WebEl[] children)
    {
        Field = field;
        Children = children;
        Type = JsonType.Array;
    }
}
