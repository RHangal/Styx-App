// profile.service.ts
import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environment';

@Injectable({
  providedIn: 'root',
})
export class ProfileService {
  private baseUrl = environment.apiUrl;

  constructor(private http: HttpClient) {}

  // Fetch profile by Auth0 user ID
  getUserProfile(auth0UserId: string): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}profile/${auth0UserId}`);
  }

  // Update profile by Auth0 user ID
  updateUserProfile(auth0UserId: string, profileData: any): Observable<any> {
    return this.http.put<any>(
      `${this.baseUrl}profile/${auth0UserId}`,
      profileData
    );
  }
  // Function to upload media to Blob Storage
  uploadMedia(file: File, auth0UserId: string): Observable<any> {
    const formData = new FormData();
    formData.append('file', file);
    formData.append('auth0UserId', auth0UserId); // Add Auth0UserId to form data
    formData.append('profile', 'pfp');
    return this.http.post(`${this.baseUrl}media/upload`, formData, {
      headers: new HttpHeaders({}),
    });
  }

  updateProfilePhoto(auth0UserId: string, photoUrl: string): Observable<any> {
    const body = { photoUrl };
    console.log(auth0UserId);
    console.log(body);
    return this.http.put(`${this.baseUrl}profile/media/${auth0UserId}`, body, {
      headers: new HttpHeaders({ 'Content-Type': 'application/json' }),
    });
  }

  uploadNewProfilePhoto(auth0UserId: string, MediaFile: File): Observable<any> {
    return new Observable((observer) => {
      console.log('Starting uploadNewProfilePhoto...');
      console.log('Pfp data:', {
        auth0UserId,
      });
      this.uploadMedia(MediaFile, auth0UserId).subscribe({
        next: (mediaResponse) => {
          const photoUrl = mediaResponse.url; // Assuming backend returns the media link
          // Step 3: Update the post with the media link
          this.updateProfilePhoto(auth0UserId, photoUrl).subscribe({
            next: (updateResponse) => {
              observer.next(updateResponse); // Final success response
              observer.complete();
            },
            error: (error) => {
              observer.error(`Error updating profile with media: ${error}`);
            },
          });
        },
        error: (error) => {
          observer.error(`Error uploading media: ${error}`);
        },
      });
    });
  }
}
