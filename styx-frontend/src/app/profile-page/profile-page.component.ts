import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { ProfileService } from './profile.service';
import { AuthService } from '@auth0/auth0-angular';
import { FormsModule } from '@angular/forms';
import { NavbarService } from '../navbar.service';

@Component({
  selector: 'app-profile-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './profile-page.component.html',
  styleUrl: './profile-page.component.sass',
})
export class ProfilePageComponent implements OnInit {
  profile: any = {};
  isEditing: boolean = false;
  selectedFile: File | undefined = undefined;
  profilePhotoUrl: string | null = null;
  isEditingPhoto: boolean = false; // Toggle for showing upload form
  showingProfile: boolean = true;
  isLoading: boolean = false; // Flag to track loading state

  constructor(
    private profileService: ProfileService,
    public auth: AuthService,
    private navbarService: NavbarService // Inject the shared service
  ) {}

  ngOnInit(): void {
    this.auth.user$.subscribe((user) => {
      if (user && user.sub) {
        const auth0UserId = user.sub;
        this.loadUserProfile(auth0UserId);
      }
    });
  }

  loadUserProfile(auth0UserId: string): void {
    this.profileService.getUserProfile(auth0UserId).subscribe(
      (data) => {
        this.profile = data;

        // Save name and email to localStorage for future use
        this.saveUserProfileToLocalStorage(data);
        // Trigger navbar refresh on profile page load
        this.navbarService.triggerNavbarRefresh();
      },
      (error) => {
        console.error('Error fetching profile:', error);
      }
    );
  }

  saveUserProfileToLocalStorage(profileData: any): void {
    if (profileData && profileData.PhotoUrl) {
      localStorage.setItem('pfp', profileData.PhotoUrl);
    }

    // Only store the name and email, if not already in localStorage
    if (profileData && profileData.Name) {
      localStorage.setItem('userName', profileData.Name);
    }

    // Only store the email once, if it doesn't exist in localStorage already
    if (
      profileData &&
      profileData.email &&
      !localStorage.getItem('userEmail')
    ) {
      localStorage.setItem('userEmail', profileData.email);
    }
  }

  saveProfile(): void {
    console.log('Attempting to save profile...');
    this.auth.user$.subscribe({
      next: (user) => {
        if (user && user.sub) {
          this.profileService
            .updateUserProfile(user.sub, this.profile)
            .subscribe({
              next: (response) => {
                console.log('Profile saved successfully:', response);
                this.isEditing = false; // After saving, exit editing mode
                this.saveUserProfileToLocalStorage(this.profile);
              },
              error: (error) => {
                console.error('Error updating profile:', error);
              },
            });
        }
      },
      error: (error) => {
        console.error('Error fetching user:', error);
      },
    });
  }

  showProfile(): void {
    this.showingProfile = true;
  }
  showBadges(): void {
    this.showingProfile = false;
  }

  toggleEdit(): void {
    console.log('Toggling edit mode');
    this.isEditing = !this.isEditing;
  }

  togglePhotoEdit(): void {
    this.isEditingPhoto = !this.isEditingPhoto; // Toggle visibility
  }

  // Handle file selection
  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.selectedFile = input.files[0];
    }
  }

  uploadProfilePhoto(): void {
    if (this.selectedFile) {
      this.auth.user$.subscribe((user) => {
        if (user && user.sub) {
          const auth0UserId = user.sub;

          if (this.selectedFile) {
            this.isLoading = true;
            this.profileService
              .uploadNewProfilePhoto(auth0UserId, this.selectedFile)
              .subscribe({
                next: (response: any) => {
                  this.isLoading = false;
                  this.profilePhotoUrl = response.photoUrl; // Update the displayed photo URL
                  this.isEditingPhoto = false; // Hide upload form after success
                  this.loadUserProfile(auth0UserId);
                  alert('Profile photo updated successfully!');
                },
                error: (error) => {
                  this.isLoading = false;
                  console.error('Error updating profile photo:', error);
                  alert('Failed to update profile photo.');
                },
              });
          }
        }
      });
    } else {
      alert('Please select a file to upload.');
    }
  }
}
