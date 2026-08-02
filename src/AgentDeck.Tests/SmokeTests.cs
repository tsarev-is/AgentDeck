using NUnit.Framework;

namespace AgentDeck.Tests;

/// <summary>
/// Проверка работоспособности тестовой инфраструктуры.
/// </summary>
[TestFixture]
public class SmokeTests
{
    /// <summary>
    /// Тестовый проект собирается и запускается.
    /// </summary>
    [Test]
    public void TestInfrastructure_Works()
    {
        Assert.Pass();
    }
}
