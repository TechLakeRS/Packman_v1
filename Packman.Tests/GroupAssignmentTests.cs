using Packman.Helpers;
using Packman.Models;
using Xunit;

namespace Packman.Tests;

public class GroupAssignmentNamerTests
{
    [Fact]
    public void Build_replaces_every_token()
        => Assert.Equal("Contoso_Reader_1.2.3_Install",
            GroupAssignmentNamer.Build("%vendor%_%appName%_%appVersion%_Install", "Contoso", "Reader", "1.2.3"));

    [Fact]
    public void Build_matches_tokens_regardless_of_case()
        => Assert.Equal("Contoso Reader",
            GroupAssignmentNamer.Build("%VENDOR% %AppName%", "Contoso", "Reader", "1.0"));

    [Fact]
    public void Build_returns_empty_for_a_blank_template()
        => Assert.Equal("", GroupAssignmentNamer.Build("   ", "Contoso", "Reader", "1.0"));
}

/// <summary>
/// The upload skips assignment unless this says there is something to assign, so a
/// missing option here silently drops a configured feature. Each is covered.
/// </summary>
public class GroupAssignmentConfigTests
{
    [Fact]
    public void HasAnyAssignment_is_false_when_nothing_is_configured()
        => Assert.False(new AppSettings.GroupAssignmentConfig().HasAnyAssignment());

    [Fact]
    public void HasAnyAssignment_sees_the_install_group()
        => Assert.True(new AppSettings.GroupAssignmentConfig { CreateGroupPerPackage = true }.HasAnyAssignment());

    [Fact]
    public void HasAnyAssignment_sees_the_uninstall_group()
        => Assert.True(new AppSettings.GroupAssignmentConfig { CreateUninstallGroupPerPackage = true }.HasAnyAssignment());

    [Fact]
    public void HasAnyAssignment_sees_existing_groups()
    {
        var config = new AppSettings.GroupAssignmentConfig
        {
            ExistingGroups = { new AppSettings.ExistingGroupAssignment { GroupName = "All Workstations" } }
        };
        Assert.True(config.HasAnyAssignment());
    }

    [Fact]
    public void Clone_copies_every_field()
    {
        var original = new AppSettings.GroupAssignmentConfig
        {
            CreateGroupPerPackage = true,
            GroupNameTemplate = "%vendor%_Install",
            NewGroupIntent = AssignmentIntent.Available,
            CreateUninstallGroupPerPackage = true,
            UninstallGroupNameTemplate = "%vendor%_Uninstall",
            ExistingGroups = { new AppSettings.ExistingGroupAssignment { GroupName = "Pilot", Intent = AssignmentIntent.Required } }
        };

        var copy = original.Clone();

        Assert.True(copy.CreateGroupPerPackage);
        Assert.Equal("%vendor%_Install", copy.GroupNameTemplate);
        Assert.Equal(AssignmentIntent.Available, copy.NewGroupIntent);
        Assert.True(copy.CreateUninstallGroupPerPackage);
        Assert.Equal("%vendor%_Uninstall", copy.UninstallGroupNameTemplate);
        Assert.Equal("Pilot", Assert.Single(copy.ExistingGroups).GroupName);
    }

    [Fact]
    public void Clone_does_not_share_the_existing_group_list()
    {
        var original = new AppSettings.GroupAssignmentConfig
        {
            ExistingGroups = { new AppSettings.ExistingGroupAssignment { GroupName = "Pilot" } }
        };

        var copy = original.Clone();
        copy.ExistingGroups.Clear();

        // The upload clears its copy; the saved settings must survive that.
        Assert.Single(original.ExistingGroups);
    }
}
