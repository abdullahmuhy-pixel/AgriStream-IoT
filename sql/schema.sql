-- AgriStream IoT - Azure SQL Database schema
-- Run in the Azure portal Query editor (or SSMS / Azure Data Studio) against database "agristreamdb".
-- The web app also creates these tables automatically on first start if the database is empty,
-- and seeds the demo users (admin / farmer / agronomist) with hashed passwords.

-- ---------- User management (Task b) ----------
CREATE TABLE dbo.Roles (
    RoleId    INT IDENTITY(1,1) PRIMARY KEY,
    RoleName  NVARCHAR(50) NOT NULL CONSTRAINT UQ_Roles_RoleName UNIQUE
);

CREATE TABLE dbo.Users (
    UserId        INT IDENTITY(1,1) PRIMARY KEY,
    Username      NVARCHAR(100) NOT NULL CONSTRAINT UQ_Users_Username UNIQUE,
    PasswordHash  NVARCHAR(512) NOT NULL,          -- salted PBKDF2 hash, never plain text
    RoleId        INT NOT NULL CONSTRAINT FK_Users_Roles REFERENCES dbo.Roles(RoleId),
    IsActive      BIT NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT 1,
    CreatedAt     DATETIME2 NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT SYSUTCDATETIME()
);

-- ---------- Device master registry (updated by the Queue-trigger function) ----------
CREATE TABLE dbo.Devices (
    DeviceId         NVARCHAR(50)  NOT NULL PRIMARY KEY,
    FieldId          NVARCHAR(50)  NOT NULL,
    DeviceName       NVARCHAR(100) NOT NULL,
    Status           NVARCHAR(30)  NOT NULL CONSTRAINT DF_Devices_Status DEFAULT 'Idle',
    IrrigationUntil  DATETIME2 NULL,
    LastCommandAt    DATETIME2 NULL,
    LastUpdatedBy    NVARCHAR(100) NULL
);
CREATE INDEX IX_Devices_FieldId ON dbo.Devices(FieldId);

CREATE TABLE dbo.IrrigationLog (
    LogId            INT IDENTITY(1,1) PRIMARY KEY,
    CommandId        UNIQUEIDENTIFIER NOT NULL CONSTRAINT UQ_IrrigationLog_CommandId UNIQUE,
    DeviceId         NVARCHAR(50)  NOT NULL,
    FieldId          NVARCHAR(50)  NOT NULL,
    DurationMinutes  INT NOT NULL,
    Reason           NVARCHAR(200) NULL,
    RequestedBy      NVARCHAR(100) NULL,
    RequestedAt      DATETIME2 NOT NULL,
    ProcessedAt      DATETIME2 NOT NULL
);

-- ---------- Seed data ----------
INSERT INTO dbo.Roles (RoleName) VALUES ('Admin'), ('Farmer'), ('Agronomist');

INSERT INTO dbo.Devices (DeviceId, FieldId, DeviceName) VALUES
    ('valve-01', 'field-01', 'North Pivot Valve'),
    ('valve-02', 'field-01', 'South Drip Valve'),
    ('valve-03', 'field-02', 'Orchard Drip Valve'),
    ('valve-04', 'field-03', 'Maize Block Pivot');

-- ---------- Evidence queries for screenshots ----------
-- SELECT u.UserId, u.Username, r.RoleName, u.IsActive, u.CreatedAt FROM dbo.Users u JOIN dbo.Roles r ON r.RoleId = u.RoleId;
-- SELECT * FROM dbo.Devices;
-- SELECT TOP 20 * FROM dbo.IrrigationLog ORDER BY ProcessedAt DESC;
