using System.Linq;
using System.Threading.Tasks;
using Content.Server._NF.Bank;
using Content.Server._NF.SectorServices;
using Content.Server._NF.Station.Components;
using Content.Server._WG.StationRoles;
using Content.Server.Afk;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WG.StationRoles;
using Content.Shared.CCVar;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.StationRoles;

[TestFixture]
public sealed class WGStationSalaryTest
{
    private static ProtoId<WGStationSalaryPrototype> ContractorProto => "Contractor";
    private static ProtoId<WGStationSalaryPrototype> DeputyProto => "Deputy";

    /// <summary>Every salary belongs to a real job, and free agents stay unpaid.</summary>
    [Test]
    public async Task SalariesNameRealJobs()
    {
        await using var pair = await PoolManager.GetServerClient();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(prototypes.EnumeratePrototypes<WGStationSalaryPrototype>(), Is.Not.Empty);
            foreach (var salary in prototypes.EnumeratePrototypes<WGStationSalaryPrototype>())
            {
                Assert.That(prototypes.HasIndex<JobPrototype>(salary.ID), Is.True, $"{salary.ID} is not a job.");
                Assert.That(salary.Wage, Is.GreaterThan(0), salary.ID);
            }
            Assert.That(prototypes.HasIndex<WGStationSalaryPrototype>(ContractorProto), Is.False);
            // Wages are paid without drawing on a sector account.
            foreach (var salary in prototypes.EnumeratePrototypes<WGStationSalaryPrototype>())
            {
                Assert.That(salary.Account, Is.EqualTo(SectorBankAccount.Invalid), $"{salary.ID} draws on {salary.Account}.");
            }
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>A station job earns; the same job on a player ship doesn't, nor does an unpaid job.</summary>
    [Test]
    public async Task OnlyStationJobsEarn()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var salaries = server.System<WGStationSalarySystem>();
        await server.WaitAssertion(() =>
        {
            var station = server.EntMan.Spawn();
            Assert.That(salaries.SalaryFor("Deputy", station)?.ID, Is.EqualTo("Deputy"));
            Assert.That(salaries.SalaryFor("Contractor", station), Is.Null);
            Assert.That(salaries.SalaryFor(null, station), Is.Null);
            var ship = server.EntMan.Spawn();
            server.EntMan.AddComponent<ExtraShuttleInformationComponent>(ship);
            Assert.That(salaries.SalaryFor("Deputy", ship), Is.Null);
            server.EntMan.DeleteEntity(station);
            server.EntMan.DeleteEntity(ship);
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>A full pay period of duty pays the whole wage without touching the faction's account; the dead aren't paid.</summary>
    [Test]
    public async Task PaysDutyTimeInFull()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var em = server.EntMan;
        var salaries = server.System<WGStationSalarySystem>();
        var bank = server.System<BankSystem>();
        var ticker = server.System<GameTicker>();
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var afk = server.ResolveDependency<IAfkManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var preset = ticker.Preset;
        var autoVote = server.CfgMan.GetCVar(CCVars.AutoVoteEnabled);
        var worker = (await server.AddDummySessions(1)).Single();
        await PoolManager.WaitUntil(server, () => preferences.HavePreferencesLoaded(worker));
        var slot = 0;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, false);
            var prefs = preferences.GetPreferences(worker.UserId);
            slot = prefs.SelectedCharacterIndex;
            preferences.SetProfile(worker.UserId, slot, ((HumanoidCharacterProfile) prefs.SelectedCharacter).WithBankBalance(0)).Wait();
            ticker.SetGamePreset("Sandbox");
            ticker.StartRound(force: true);
            if (!server.System<SectorServiceSystem>().GetServiceEntity().Valid)
                em.AddComponent<StationSectorServiceHostComponent>(em.SpawnEntity(null, MapCoordinates.Nullspace));
        });
        await pair.RunTicksSync(5);
        var map = await pair.CreateTestMap();

        EntityUid body = default;
        var wage = 0;
        await server.WaitPost(() =>
        {
            wage = prototypes.Index<WGStationSalaryPrototype>(DeputyProto).Wage / 4;
            ticker.PlayerJoinGame(worker, silent: true);
            body = em.SpawnEntity("MobHuman", map.GridCoords);
            var minds = server.System<SharedMindSystem>();
            minds.TransferTo(minds.CreateMind(worker.UserId, "Dee Deputy"), body);
            salaries.AssignRole(body, worker, "Deputy", slot);
            // Dummy sessions send no input, so mark the worker active.
            afk.PlayerDidAction(worker);
        });
        await pair.RunTicksSync(5);

        int Paid() => ((HumanoidCharacterProfile) preferences.GetPreferences(worker.UserId).Characters[slot]).BankBalance;
        int Balance()
        {
            Assert.That(bank.TryGetBalance(SectorBankAccount.Nfsd, out var balance), Is.True);
            return balance;
        }

        var start = 0;
        await server.WaitAssertion(() =>
        {
            start = Balance();
            // The system's own once-a-minute sample may have run during the ticks above; count from nothing.
#pragma warning disable RA0002 // The test resets the duty count directly.
            em.GetComponent<WGStationRoleComponent>(body).UnpaidSeconds = 0;
#pragma warning restore RA0002
            salaries.SampleDuty(WGStationSalarySystem.PaySeconds - 60);
            Assert.That(Paid(), Is.EqualTo(0), "No salary before a full pay period.");
            salaries.SampleDuty(60);
            Assert.That(Paid(), Is.EqualTo(wage));
            Assert.That(Balance(), Is.EqualTo(start), "The salary doesn't come out of the faction's account.");
        });

        await server.WaitAssertion(() =>
        {
            // An empty faction account doesn't cut the wage.
            Assert.That(bank.TrySectorWithdraw(SectorBankAccount.Nfsd, Balance(), LedgerEntryType.StationWithdrawalOther), Is.True);
            salaries.SampleDuty(WGStationSalarySystem.PaySeconds);
            Assert.That(Paid(), Is.EqualTo(wage * 2), "An empty account cut the wage.");

            var mobs = server.System<MobStateSystem>();
            mobs.ChangeMobState(body, MobState.Dead);
            salaries.SampleDuty(WGStationSalarySystem.PaySeconds);
            Assert.That(Paid(), Is.EqualTo(wage * 2), "The dead aren't on duty.");
        });

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, autoVote);
            if (preset != null)
                ticker.SetGamePreset(preset);
            ticker.RestartRound();
        });
        await pair.RunTicksSync(5);
        await pair.CleanReturnAsync();
    }
}
