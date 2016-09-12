This VS extension demonstrates several of the Team Explorer extensibility points.

This project adds a new section to the Pending Changes page titled "Recommended Reviewers".  The section displays additional information about the
items in the Included Changes section.  
1. List of recommended reviewers are displayed in their order of preference (based on no. of commits and reviews on the files in the changeset)
2. On clicking on a file in the pending changes section, you can get the list of reviewers and commiters for that particular file

> P.S. - This plugin is still pre-alpha and not very performant on slow internet connections


HOW to SETUP and run the code 
1. Prerequisites -
    - Install VS 2015 with Visual Studio Extension SDK
    - Install Extensibility Tools for Visual Studio by Mads Kristensen (https://visualstudiogallery.msdn.microsoft.com/ab39a092-1343-46e2-b0f1-6a3f91155aa6)
2. Open the only solution and build the code 
3. Runing the code (using F5) should bring up the extension in the "Pending Changes" section of Team Explorer