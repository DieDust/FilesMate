# Archive interoperability fixtures

These small archives come from the SharpCompress 0.50.4 test corpus:
https://github.com/adamhathcock/sharpcompress/tree/0.50.4/tests/TestArchives/Archives

Copyright (c) 2014 Adam Hathcock and contributors. MIT license; the full text is
preserved in `installer/ThirdPartyNotices/SharpCompress-LICENSE.txt`.

The encrypted 7z fixture uses `testpassword`; the encrypted RAR5, ZIP AES and
ZIP BZip2/PKWARE fixtures use `test`. The tests verify missing and wrong passwords,
successful extraction, solid RAR and the legacy `.z01` / `.zip` volume ordering.
Other tests generate their own Chinese-name ZIP/7z and renamed numerical splits.
