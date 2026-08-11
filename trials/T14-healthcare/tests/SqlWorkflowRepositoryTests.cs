using System;
using System.IO;
using Microsoft.Data.SqlClient;
using Xunit;
using DataAccess;
using _module;

namespace DataAccess.Tests
{
    public class SqlWorkflowRepositoryTests
    {
        [Fact]
        public void Constructor_NullConnectionString_UsesDefault()
        {
            // Arrange
            Environment.SetEnvironmentVariable("WorkflowDb__ConnectionString", null);

            // Act
            var repo = new SqlWorkflowRepository(null);

            // Assert
            Assert.NotNull(repo);
        }

        [Fact]
        public void Constructor_EmptyConnectionString_UsesDefault()
        {
            // Arrange
            Environment.SetEnvironmentVariable("WorkflowDb__ConnectionString", "");

            // Act
            var repo = new SqlWorkflowRepository("");

            // Assert
            Assert.NotNull(repo);
        }

        [Fact]
        public void Constructor_WhitespaceConnectionString_UsesDefault()
        {
            // Arrange
            Environment.SetEnvironmentVariable("WorkflowDb__ConnectionString", "   ");

            // Act
            var repo = new SqlWorkflowRepository("   ");

            // Assert
            Assert.NotNull(repo);
        }

        [Fact]
        public void LoadWorkflow_InvalidConnectionString_ReturnsFailure()
        {
            // Arrange
            var repo = new SqlWorkflowRepository("Server=invalid;Database=invalid;User Id=invalid;Password=invalid;TrustServerCertificate=True");

            // Act
            var result = repo.LoadWorkflow("nonexistent");

            // Assert
            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Error);
        }

        [Fact]
        public void LoadWorkflow_NullWorkflowId_ReturnsFailure()
        {
            // Arrange
            var repo = new SqlWorkflowRepository("Server=invalid;Database=invalid;User Id=invalid;Password=invalid;TrustServerCertificate=True");

            // Act
            var result = repo.LoadWorkflow(null);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Error);
        }

        [Fact]
        public void LoadWorkflow_EmptyWorkflowId_ReturnsFailure()
        {
            // Arrange
            var repo = new SqlWorkflowRepository("Server=invalid;Database=invalid;User Id=invalid;Password=invalid;TrustServerCertificate=True");

            // Act
            var result = repo.LoadWorkflow("");

            // Assert
            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Error);
        }

        [Fact]
        public void SaveWorkflow_InvalidConnectionString_ReturnsFailure()
        {
            // Arrange
            var repo = new SqlWorkflowRepository("Server=invalid;Database=invalid;User Id=invalid;Password=invalid;TrustServerCertificate=True");
            var instance = new WorkflowInstance("wf1", "phase1", "data");

            // Act
            var result = repo.SaveWorkflow(instance);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Error);
        }

        [Fact]
        public void SaveWorkflow_NullInstance_ReturnsFailure()
        {
            // Arrange
            var repo = new SqlWorkflowRepository("Server=invalid;Database=invalid;User Id=invalid;Password=invalid;TrustServerCertificate=True");

            // Act
            var result = repo.SaveWorkflow(null);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Error);
        }

        [Fact]
        public void SaveWorkflow_InstanceWithNullHandoffData_ReturnsFailure()
        {
            // Arrange
            var repo = new SqlWorkflowRepository("Server=invalid;Database=invalid;User Id=invalid;Password=invalid;TrustServerCertificate=True");
            var instance = new WorkflowInstance("wf1", "phase1", null);

            // Act
            var result = repo.SaveWorkflow(instance);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Error);
        }

        [Fact]
        public void SaveWorkflow_InstanceWithEmptyWorkflowId_ReturnsFailure()
        {
            // Arrange
            var repo = new SqlWorkflowRepository("Server=invalid;Database=invalid;User Id=invalid;Password=invalid;TrustServerCertificate=True");
            var instance = new WorkflowInstance("", "phase1", "data");

            // Act
            var result = repo.SaveWorkflow(instance);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Error);
        }

        [Fact]
        public void SaveWorkflow_InstanceWithEmptyPhase_ReturnsFailure()
        {
            // Arrange
            var repo = new SqlWorkflowRepository("Server=invalid;Database=invalid;User Id=invalid;Password=invalid;TrustServerCertificate=True");
            var instance = new WorkflowInstance("wf1", "", "data");

            // Act
            var result = repo.SaveWorkflow(instance);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Error);
        }

        [Fact]
        public void SaveWorkflow_InstanceWithEmptyHandoffData_ReturnsFailure()
        {
            // Arrange
            var repo = new SqlWorkflowRepository("Server=invalid;Database=invalid;User Id=invalid;Password=invalid;TrustServerCertificate=True");
            var instance = new WorkflowInstance("wf1", "phase1", "");

            // Act
            var result = repo.SaveWorkflow(instance);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Error);
        }
    }
}