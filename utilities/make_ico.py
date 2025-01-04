from PIL import Image # Pillow library is required

# Open the existing PNG image
image = Image.open('icon.png')  # Replace with your image path

# Resize the image to 32x32
icon = image.resize((128, 128), Image.ADAPTIVE)

# Save the image as an ICO file
icon.save('app_.ico', format='ICO')

print("ICO file created successfully!")
