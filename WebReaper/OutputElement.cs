namespace WebReaper;

public class WebEl {

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
        this.Field = field;
        this.Selector = selector;
        this.Type = type;
    }

    public WebEl(
        string field,
        params WebEl[] children)
    {
        this.Field = field;
        this.Children = children;
        this.Type = JsonType.Array;
    }
}
