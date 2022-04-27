namespace WebReaper.Domain.Schema;

using HtmlAgilityPack;
using WebReaper.Domain.Selectors;

public abstract record SchemaElement(string Field, string? Selector = null, SelectorType SelectorType = SelectorType.Css) {
    public abstract string GetData(HtmlDocument doc);
};
