namespace WebReaper.Domain.PageActions;

public record PageAction(PageActionType Type, params object[] Parameters);