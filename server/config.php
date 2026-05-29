<?php
// ── Database config ──────────────────────────────────────────────────────────
const DB_HOST = 'localhost';       // ← your MySQL hostname (You probably dont need to change this)
const DB_NAME = 'securechannel';   // ← the name of the MySQL database to use. (This must exist! Create it first by executing setup.sql)
const DB_USER = 'admin';       // ← your MySQL username
const DB_PASS = 'admin';   // ← your MySQL password

const WURMSHARE_CURRENT_VERSION = '1.0.0';       // ← The version of the client the server expects
const WURMSHARE_MINIMUM_VERSION = '1.0.0';   // ← The oldest version of the client which is still compatible with future versions which may mix with it.
