-- ===========================================================================
-- setup.sql  —  SecureChannel database schema
-- ===========================================================================
-- Run this once before deploying the API.
--
-- Usage:
--   mysql -u root -p < setup.sql
--
-- Or paste into phpMyAdmin / MySQL Workbench.
-- ===========================================================================

-- Create the database (skip if it already exists)
CREATE DATABASE IF NOT EXISTS securechannel
    CHARACTER SET  utf8mb4
    COLLATE        utf8mb4_unicode_ci;

USE securechannel;

-- ---------------------------------------------------------------------------
-- channels
-- Stores one row per channel.  channel_id is the public identifier (
-- SHA-256 of the password). auth_token is SHA-256(channel_id) and is the
-- proof the client submits; neither value reveals the original password.
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS channels (
    id          INT UNSIGNED NOT NULL AUTO_INCREMENT,
    channel_id  CHAR(64)     NOT NULL  COMMENT 'Hex(SHA-256(password))',
    auth_token  CHAR(64)     NOT NULL  COMMENT 'Hex(SHA-256(channel_id_bytes))',
    created_at  DATETIME     NOT NULL,

    PRIMARY KEY (id),

    -- Enforces uniqueness and enables O(1) lookup by channel_id
    UNIQUE KEY uq_channel_id (channel_id)

) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_unicode_ci
  COMMENT='One row per encrypted channel';


-- ---------------------------------------------------------------------------
-- messages
-- Log of all encrypted messages.  encrypted_data is opaque Base64 ciphertext
-- — the server has no AES key and cannot read it.
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS messages (
    id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    channel_id      CHAR(64)        NOT NULL  COMMENT 'FK-equivalent to channels.channel_id',
    encrypted_data  MEDIUMTEXT      NOT NULL  COMMENT 'AES-256-CBC ciphertext, Base64-encoded',
    created_at      DATETIME        NOT NULL,

    PRIMARY KEY (id),

    -- Composite index covering the core polling query:
    --   WHERE channel_id = ? AND id > ? ORDER BY id ASC LIMIT ?
    -- This makes incremental polling O(log n + k) rather than O(n).
    KEY idx_channel_since (channel_id, id),

    -- Secondary index for time-range or audit queries
    KEY idx_channel_time  (channel_id, created_at)

) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_unicode_ci
  COMMENT='Log of all encrypted messages; server cannot decrypt these'
  ROW_FORMAT=DYNAMIC;   -- allows large MEDIUMTEXT values to be stored off-page efficiently


-- ---------------------------------------------------------------------------
-- Optional: create a dedicated application user with minimum privileges.
-- Replace 'app_password' with a strong, random password.
-- ---------------------------------------------------------------------------
-- CREATE USER  IF NOT EXISTS 'securechannel_app'@'localhost' IDENTIFIED BY 'app_password';
-- GRANT SELECT, INSERT ON securechannel.channels TO 'securechannel_app'@'localhost';
-- GRANT SELECT, INSERT ON securechannel.messages  TO 'securechannel_app'@'localhost';
-- FLUSH PRIVILEGES;
