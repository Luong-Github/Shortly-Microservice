q-- SQL Server initialization script
-- This runs automatically when the container starts

-- Create database if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'UrlShortener')
BEGIN
    CREATE DATABASE [UrlShortener];
    PRINT 'Database UrlShortener created successfully';
END
ELSE
BEGIN
    PRINT 'Database UrlShortener already exists';
END

IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'IdentityDB')
BEGIN
    CREATE DATABASE [IdentityDB];
    PRINT 'Database IdentityDB created successfully';
END
ELSE
BEGIN
    PRINT 'Database IdentityDB already exists';
END

-- Set recovery model
ALTER DATABASE [UrlShortener] SET RECOVERY SIMPLE;
