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
  styleUrls: ['./profile-page.component.sass'],
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
    private navbarService: NavbarService // Shared service for navbar refresh
  ) {}

  ngOnInit(): void {
    // Wait for the user object from Auth0
    this.auth.user$.subscribe((user) => {
      if (user) {
        // If logged in, load the user profile from the backend
        this.loadUserProfile();
      }
    });
  }

  /**
   * Loads the user profile by calling ProfileService.getUserProfile(),
   * which uses the JWT token to derive the user ID (sub).
   */
  loadUserProfile(): void {
    this.profileService.getUserProfile().subscribe({
      next: (data) => {
        this.profile = data;
        // Save user info to local storage for future use
        this.saveUserProfileToLocalStorage(data);
        // Trigger navbar refresh on profile page load
        this.navbarService.triggerNavbarRefresh();
      },
      error: (err) => {
        console.error('Error fetching profile:', err);
      },
    });
  }

  /**
   * Saves certain profile fields to local storage
   * (e.g., photo URL, name, email) if available.
   */
  saveUserProfileToLocalStorage(profileData: any): void {
    if (profileData?.PhotoUrl) {
      localStorage.setItem('pfp', profileData.PhotoUrl);
    }
    if (profileData?.Name) {
      localStorage.setItem('userName', profileData.Name);
    }
    if (profileData?.email && !localStorage.getItem('userEmail')) {
      localStorage.setItem('userEmail', profileData.email);
    }
  }

  /**
   * Saves edits to the profile using the new token-based approach.
   */
  saveProfile(): void {
    console.log('Attempting to save profile...');
    this.isEditing = false; // Immediately exit editing mode (you can adjust the timing)
    this.profileService.updateUserProfile(this.profile).subscribe({
      next: (response) => {
        console.log('Profile saved successfully:', response);
        // Optionally reload or refresh local storage
        this.saveUserProfileToLocalStorage(this.profile);
      },
      error: (error) => {
        console.error('Error updating profile:', error);
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

  /**
   * Handle file selection for the profile photo upload.
   */
  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.selectedFile = input.files[0];
    }
  }

  /**
   * Uploads a new profile photo:
   * 1) uploadNewProfilePhoto -> uploads the file, returns a URL,
   * 2) updates the user's photo in DB,
   * 3) refreshes the UI.
   */
  uploadProfilePhoto(): void {
    if (!this.selectedFile) {
      alert('Please select a file to upload.');
      return;
    }

    this.isLoading = true; // Show loading indicator
    this.profileService.uploadNewProfilePhoto(this.selectedFile).subscribe({
      next: (response: any) => {
        this.isLoading = false;
        this.profilePhotoUrl = response.photoUrl; // Possibly the backend returns 'photoUrl'
        this.isEditingPhoto = false; // Hide upload form after success
        // Reload the user profile to get the new PhotoUrl
        this.loadUserProfile();
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
