import json
import msvcrt

def FileChange(func, *args, **kwargs):
  """ Locks a file and performs operations on it """
  with open('./resource/settings.json', 'r+') as file:
    try:
      msvcrt.locking(file.fileno(), msvcrt.LK_LOCK, 1)

      settings = func(*args, **kwargs)

      file.seek(0)
      file.write(json.dumps(settings, indent=2))
      file.truncate()
    except Exception as e:
      pass
    
    try:
      msvcrt.locking(file.fileno(), msvcrt.LK_UNLCK, 1)
    except Exception as e:
      pass
