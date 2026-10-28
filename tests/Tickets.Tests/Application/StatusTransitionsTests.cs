using FluentAssertions;
using Tickets.Application.Tickets;
using Tickets.Domain;
using static Tickets.Domain.TicketStatus;

namespace Tickets.Tests.Application;

public sealed class StatusTransitionsTests
{
    /// <summary>All 16 (from, to) pairs with the expected answer – the full matrix, not a sample.</summary>
    public static TheoryData<TicketStatus, TicketStatus, bool> Matrix => new()
    {
        // Allowed
        { New, InProgress, true },
        { InProgress, Waiting, true },
        { InProgress, Completed, true },
        { Waiting, InProgress, true },
        { Waiting, Completed, true },
        { Completed, InProgress, true },

        // Same status – never allowed
        { New, New, false },
        { InProgress, InProgress, false },
        { Waiting, Waiting, false },
        { Completed, Completed, false },

        // Forbidden
        { New, Waiting, false },
        { New, Completed, false },
        { InProgress, New, false },
        { Waiting, New, false },
        { Completed, New, false },
        { Completed, Waiting, false },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Transition_matrix_matches_the_specification(TicketStatus from, TicketStatus to, bool expected) =>
        StatusTransitions.IsAllowed(from, to).Should().Be(expected);

    [Fact]
    public void Matrix_test_covers_every_pair()
    {
        var statuses = Enum.GetValues<TicketStatus>();
        Matrix.Select(row => ((TicketStatus)row[0], (TicketStatus)row[1])).Should()
            .BeEquivalentTo(statuses.SelectMany(from => statuses.Select(to => (from, to))));
    }

    [Fact]
    public void Allowed_targets_are_listed_per_status()
    {
        StatusTransitions.AllowedFrom(New).Should().Equal(InProgress);
        StatusTransitions.AllowedFrom(InProgress).Should().Equal(Waiting, Completed);
        StatusTransitions.AllowedFrom(Waiting).Should().Equal(InProgress, Completed);
        StatusTransitions.AllowedFrom(Completed).Should().Equal(InProgress);
    }

    [Fact]
    public void Undefined_status_has_no_allowed_transitions()
    {
        StatusTransitions.IsAllowed((TicketStatus)0, New).Should().BeFalse();
        StatusTransitions.AllowedFrom((TicketStatus)99).Should().BeEmpty();
    }
}
