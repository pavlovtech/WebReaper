namespace WebReaper.Domain.Selectors;

// ADR 0008: the Newtonsoft [JsonConverter(StringEnumConverter)] attribute was
// removed — string-enum serialisation is now the AOT-safe
// JsonStringEnumConverter<PageType> registered on WebReaperJson. The Domain
// enum no longer references Newtonsoft.
public enum PageType
{
    Dynamic,
    Static
}
