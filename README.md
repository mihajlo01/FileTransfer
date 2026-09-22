# FileTransfer

The application was developed in accordance to document specifications.

As a personal expansion, a FE component was included, developed with AngularJS based on VS's Angular template.

The solution consists of the next projects:
- filetransfer.client  -- The Web Client Application
- FileTransfer.Server -- The Web Sever Application
- FileTransfer.Server.Tests - Unit Tests project (for basic tests)
- FileTransfer.Local.Client -- The Local Console Application
- FileTransfer.Models -- Class Library project used for model referencing

In order to launch the application, follow VS's requirements for needed frameworks and component versions after pulling the solution.
- Once the Server application is started, it will automatically build and initialize the Angular component as well - opening console views for the Server and Web Client as well as the browser (currently set for Chrome and Edge) for the client.
- The Client Console application will prompt a console with detailed logs and instead of prompting a manual input for the source and destination paths; it will automatically trigger the transfer from the S/D paths set in the configuration file (appsettings.json)
- The Tests project targets the Server logic and can be run as normal unit tests.

What I would improve further:
- code cleanup and comments addition
- expansion of the unit tests
- possibly improve MD5 -> HMAC-MD5
- beautify the UI view
- extend protocol security
and other major updates...
