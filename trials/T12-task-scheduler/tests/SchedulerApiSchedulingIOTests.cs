using System;
using Xunit;
using SchedulerApi;

namespace SchedulerApi.Tests
{
    public class SchedulerApiSchedulingIOTests
    {
        [Fact]
        public void CreateAppointment_ValidInput_ReturnsNonEmptyId()
        {
            // Arrange
            var entityId = "entity-1";
            var startTime = DateTime.UtcNow.AddHours(1);
            var endTime = DateTime.UtcNow.AddHours(2);
            var provider = "provider-1";

            // Act
            var result = SchedulingIO.CreateAppointment(entityId, startTime, endTime, provider);

            // Assert
            Assert.False(string.IsNullOrEmpty(result));
        }

        [Fact]
        public void CreateAppointment_EmptyEntityId_ReturnsNonEmptyId()
        {
            // Arrange
            var entityId = "";
            var startTime = DateTime.UtcNow.AddHours(1);
            var endTime = DateTime.UtcNow.AddHours(2);
            var provider = "provider-1";

            // Act
            var result = SchedulingIO.CreateAppointment(entityId, startTime, endTime, provider);

            // Assert
            Assert.False(string.IsNullOrEmpty(result));
        }

        [Fact]
        public void CreateAppointment_NullEntityId_ReturnsNonEmptyId()
        {
            // Arrange
            string entityId = null;
            var startTime = DateTime.UtcNow.AddHours(1);
            var endTime = DateTime.UtcNow.AddHours(2);
            var provider = "provider-1";

            // Act
            var result = SchedulingIO.CreateAppointment(entityId, startTime, endTime, provider);

            // Assert
            Assert.False(string.IsNullOrEmpty(result));
        }

        [Fact]
        public void CreateAppointment_StartAfterEnd_ReturnsNonEmptyId()
        {
            // Arrange
            var entityId = "entity-1";
            var startTime = DateTime.UtcNow.AddHours(2);
            var endTime = DateTime.UtcNow.AddHours(1);
            var provider = "provider-1";

            // Act
            var result = SchedulingIO.CreateAppointment(entityId, startTime, endTime, provider);

            // Assert
            Assert.False(string.IsNullOrEmpty(result));
        }

        [Fact]
        public void GetAppointments_ValidInput_ReturnsEmptyArray()
        {
            // Arrange
            var entityId = "entity-1";
            var dateRange = "2025-01-01,2025-01-31";

            // Act
            var result = SchedulingIO.GetAppointments(entityId, dateRange);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void GetAppointments_EmptyEntityId_ReturnsEmptyArray()
        {
            // Arrange
            var entityId = "";
            var dateRange = "2025-01-01,2025-01-31";

            // Act
            var result = SchedulingIO.GetAppointments(entityId, dateRange);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void GetAppointments_NullEntityId_ReturnsEmptyArray()
        {
            // Arrange
            string entityId = null;
            var dateRange = "2025-01-01,2025-01-31";

            // Act
            var result = SchedulingIO.GetAppointments(entityId, dateRange);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void GetAppointments_EmptyDateRange_ReturnsEmptyArray()
        {
            // Arrange
            var entityId = "entity-1";
            var dateRange = "";

            // Act
            var result = SchedulingIO.GetAppointments(entityId, dateRange);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void CancelAppointment_ValidId_ReturnsTrue()
        {
            // Arrange
            var appointmentId = "appt-1";

            // Act
            var result = SchedulingIO.CancelAppointment(appointmentId);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void CancelAppointment_EmptyId_ReturnsTrue()
        {
            // Arrange
            var appointmentId = "";

            // Act
            var result = SchedulingIO.CancelAppointment(appointmentId);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void CancelAppointment_NullId_ReturnsTrue()
        {
            // Arrange
            string appointmentId = null;

            // Act
            var result = SchedulingIO.CancelAppointment(appointmentId);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void CheckAvailability_ValidInput_ReturnsTrue()
        {
            // Arrange
            var provider = "provider-1";
            var startTime = DateTime.UtcNow.AddHours(1);
            var endTime = DateTime.UtcNow.AddHours(2);

            // Act
            var result = SchedulingIO.CheckAvailability(provider, startTime, endTime);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void CheckAvailability_EmptyProvider_ReturnsTrue()
        {
            // Arrange
            var provider = "";
            var startTime = DateTime.UtcNow.AddHours(1);
            var endTime = DateTime.UtcNow.AddHours(2);

            // Act
            var result = SchedulingIO.CheckAvailability(provider, startTime, endTime);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void CheckAvailability_NullProvider_ReturnsTrue()
        {
            // Arrange
            string provider = null;
            var startTime = DateTime.UtcNow.AddHours(1);
            var endTime = DateTime.UtcNow.AddHours(2);

            // Act
            var result = SchedulingIO.CheckAvailability(provider, startTime, endTime);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void CheckAvailability_StartAfterEnd_ReturnsTrue()
        {
            // Arrange
            var provider = "provider-1";
            var startTime = DateTime.UtcNow.AddHours(2);
            var endTime = DateTime.UtcNow.AddHours(1);

            // Act
            var result = SchedulingIO.CheckAvailability(provider, startTime, endTime);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void GetAvailableSlots_ValidInput_ReturnsEmptyArray()
        {
            // Arrange
            var provider = "provider-1";
            var date = DateTime.UtcNow.Date;

            // Act
            var result = SchedulingIO.GetAvailableSlots(provider, date);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void GetAvailableSlots_EmptyProvider_ReturnsEmptyArray()
        {
            // Arrange
            var provider = "";
            var date = DateTime.UtcNow.Date;

            // Act
            var result = SchedulingIO.GetAvailableSlots(provider, date);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void GetAvailableSlots_NullProvider_ReturnsEmptyArray()
        {
            // Arrange
            string provider = null;
            var date = DateTime.UtcNow.Date;

            // Act
            var result = SchedulingIO.GetAvailableSlots(provider, date);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void GetAvailableSlots_MinDate_ReturnsEmptyArray()
        {
            // Arrange
            var provider = "provider-1";
            var date = DateTime.MinValue;

            // Act
            var result = SchedulingIO.GetAvailableSlots(provider, date);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void GetAvailableSlots_MaxDate_ReturnsEmptyArray()
        {
            // Arrange
            var provider = "provider-1";
            var date = DateTime.MaxValue;

            // Act
            var result = SchedulingIO.GetAvailableSlots(provider, date);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }
    }
}