## Communication Server Setup

# Requirements
- A Web Host with:
-- PHP 8.0+ Support
-- MySQL Support
-- A registered public domain name ("likethis.com")

It should run on anything that fits those requirements.

# Installation Steps
Upload the contents of this directory to your server somewhere inside a publicly accessible directory. ("likethis.com/directory")
Ensure file permissions are set appropriately to allow public access to all these files.

This URL is what your users will type in for their new comms server. ("likethis.com/directory")

Edit the file "config.php" - update it with your own SQL host address, username and password.

Log into your MySQL server through whichever method you choose, and execute the included "setup.sql" file.
Or, in PhPMyAdmin, you can just copy the contents into the "SQL" tab.
This will create the necessary database, table and row structure for the comms system.