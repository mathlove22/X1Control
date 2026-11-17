# GitHub upload guide

The folder `X1Control-GitHub` is the source repository. The Windows download is a separate release asset so the 178 MB executable does not become part of Git history.

## Publish the repository

1. Create a new **public** repository on GitHub. Leave “Add a README,” “Add .gitignore,” and “Choose a license” unchecked; this folder already contains the files and the first-party code is not open-source licensed.
2. In PowerShell, change to this folder and push it:

```powershell
git init -b main
git add .
git commit -m "Prepare X1 Control 1.1.3"
git remote add origin https://github.com/OWNER/REPOSITORY.git
git push -u origin main
```

Replace `OWNER/REPOSITORY` with the URL GitHub shows for the repository.

## Publish the download

1. Open the repository's **Releases** page and choose **Create a new release**.
2. Use tag `v1.1.3` and title `X1 Control 1.1.3`.
3. Attach `outputs/X1Control-Windows-x64.zip` and `outputs/X1Control-source-1.1.3.zip` from the workspace. Paste the contents of `RELEASE_NOTES.md` into the release description.
4. Publish the release. The README directs users to the Releases page.

Do not upload the preview PNG/JSON/CSV files; they are local sensor captures. The `.gitignore` also excludes build output, logs, generated settings and installers.
