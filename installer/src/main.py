""" This module is the installer for the Subconscious release distributable. """
import os
import sys
import shutil
import ctypes
import zipfile
import requests
import tempfile
import flet as ft

from titlebar import TitleBar


def is_admin():
  try:
    return ctypes.windll.shell32.IsUserAnAdmin()
  except:
    return False

def download_latest_release(download_dir, pb):
  """ Download the latest release of the Subconscious app """
  # GitHub API URL for the latest release
  url = "https://api.github.com/repos/baebranch/subconscious/releases/latest"
  
  # Send a GET request to the GitHub API
  response = requests.get(url)
  response.raise_for_status()  # Raise an exception for HTTP errors
  file_size = int(response.headers.get('Content-Length', 0))
  
  # Parse the JSON response
  release_data = response.json()
  
  # Get the URL of the asset to download (assuming the first asset)
  asset = release_data['assets'][0]
  download_url = asset['browser_download_url']
  asset_name = asset['name']
  
  # Download the asset
  download_response = requests.get(download_url, stream=True)
  download_response.raise_for_status()
  
  # Save the asset to the specified directory
  file_path = os.path.join(download_dir, asset_name)
  with open(file_path, 'wb') as file:
    for chunk in download_response.iter_content(chunk_size=8192):
      file.write(chunk)

  return file_path


def extract_to_program_files(zip_path, program_files_dir):
  """ Extract the downloaded zip file to the Program Files directory """
  with zipfile.ZipFile(zip_path, 'r') as zip_ref:
    zip_ref.extractall(program_files_dir)


def main(page: ft.Page):
  """ Main function to initiate the installer UI """
  # Window size
  page.visible = False
  page.window.width = page.window.min_width = page.window.max_width = 450
  page.window.height = page.window.min_height = page.window.max_height = 250
  page.window.center()
  page.padding = 0
  page.spacing = 0
  page.window.frameless = False
  page.window.title_bar_hidden = True
  page.theme_mode = ft.ThemeMode.SYSTEM

  # UI Config
  done = ft.TextButton("Done", on_click=lambda _: page.window.close(), style=ft.ButtonStyle(shape=ft.RoundedRectangleBorder(radius=5)), visible=False)
  step = ft.Text("Installing...", size=15)
  pb = ft.ProgressBar(width=430, bar_height=5, height=5)

  # Layout
  page.add(TitleBar(page, title="Subconscious Installer"))
  page.add(ft.Container(content=
    ft.Column(
    [
      ft.Row([
        ft.Text("Subconscious", size=25, color=ft.Colors.PRIMARY),
      ], alignment="left", spacing=0),
      step,
      pb,
      ft.Row([
        done
      ], expand=True)
    ],
    spacing=20, expand=True, alignment=ft.alignment.top_center
  ),
  padding=ft.padding.only(20, 20, 20, 20))
  )
  page.visible = True
  page.update()

  try:
    # Create a temporary directory
    with tempfile.TemporaryDirectory() as temp_dir:
      # Download the latest release to the temporary directory
      step.value = "Downloading..."
      pb.value = 0.0
      page.update()
      zip_path = download_latest_release(temp_dir, pb)
      
      # Define the Program Files directory
      program_files_dir = os.path.join(os.environ['ProgramFiles'], 'Subconscious')
      step.value = f"Installing to {program_files_dir}..."
      pb.value = 0.50
      page.update()
      
      # Ensure the Program Files directory exists
      os.makedirs(program_files_dir, exist_ok=True)
      
      # Extract the downloaded zip file to the Program Files directory
      step.value = "Extracting files..."
      pb.value = 0.75
      page.update()
      extract_to_program_files(zip_path, program_files_dir)
      done.visible = True
      step.value = "Installation completed successfully!"
      pb.value = 1.0
      page.update()
  except Exception as e:
    pb.value = 0
    done.visible = True
    step.value = "An error occurred, could not install"
    page.update()
    print(e)
  
  
if __name__ == "__main__":
  if is_admin():
    print("Already running as administrator.")
    ft.app(target=main)
  else:
    print("Attempting to run as administrator...")
    ctypes.windll.shell32.ShellExecuteW(None, "runas", sys.executable, " ".join(sys.argv), None, 1)
