using Gomoku.Application.Identity;
using Xunit;

namespace Gomoku.Application.Tests.Identity;

public sealed class OwnerSelectorTests
{
    [Fact]
    public void AccountWinsDefaultWhenBothIdentitiesExist()
    {
        var account = Guid.NewGuid();
        var guest = Guid.NewGuid();
        Assert.Equal(new OwnerIdentity(OwnerScope.Account, account),
            OwnerSelector.Select(account, guest, null));
        Assert.Equal(new OwnerIdentity(OwnerScope.Guest, guest),
            OwnerSelector.Select(account, guest, "guest"));
    }

    [Fact]
    public void MissingChosenIdentityDoesNotBorrowOtherScope()
    {
        Assert.Null(OwnerSelector.Select(Guid.NewGuid(), null, "guest"));
        Assert.Null(OwnerSelector.Select(null, Guid.NewGuid(), "account"));
    }

    [Fact]
    public void InvalidScopeIsRejected()
    {
        Assert.Throws<ArgumentException>(() => OwnerSelector.Select(null, null, "admin"));
    }
}
