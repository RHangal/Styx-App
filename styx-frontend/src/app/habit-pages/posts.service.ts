import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environment';

@Injectable({
  providedIn: 'root',
})
export class PostsService {
  private baseUrl = environment.apiUrl;

  constructor(private http: HttpClient) {}

  getPostsByType(postType: string): Observable<any> {
    const apiUrl = `${this.baseUrl}posts/${postType}`; // Replace with your actual API Gateway URL.

    return this.http.get<any[]>(apiUrl);
  }

  // Function to create a new post
  createPost(
    PostType: string,
    Name: string,
    Email: string,
    Auth0UserId: string,
    Caption: string
  ): Observable<any> {
    const body = {
      PostType,
      Name,
      Email,
      Auth0UserId,
      Caption,
    };
    console.log('Request body:', body);
    return this.http.post(`${this.baseUrl}posts`, body, {
      headers: new HttpHeaders({ 'Content-Type': 'application/json' }),
    });
  }

  // Function to upload media to Blob Storage
  uploadMedia(file: File, auth0UserId: string): Observable<any> {
    const formData = new FormData();
    formData.append('file', file);
    formData.append('auth0UserId', auth0UserId); // Add Auth0UserId to form data

    return this.http.post(`${this.baseUrl}media/upload`, formData, {
      headers: new HttpHeaders({}),
    });
  }

  // Function to update the post with the media link
  updatePostWithMedia(postId: string, mediaUrl: string): Observable<any> {
    const body = { mediaUrl };
    console.log(postId);
    console.log(body);
    return this.http.put(`${this.baseUrl}posts/media/${postId}`, body, {
      headers: new HttpHeaders({ 'Content-Type': 'application/json' }),
    });
  }

  rewardDailyCoins(auth0UserId: string): Observable<any> {
    const body = { auth0UserId };
    return this.http.post(`${this.baseUrl}users/reward-coins`, body, {
      headers: new HttpHeaders({ 'Content-Type': 'application/json' }),
    });
  }

  // Combined method to create a post and optionally upload media
  createPostWithOptionalMedia(
    PostType: string,
    Name: string,
    Email: string,
    Auth0UserId: string,
    Caption: string,
    MediaFile?: File
  ): Observable<any> {
    return new Observable((observer) => {
      console.log('Starting createPostWithOptionalMedia...');
      console.log('Post data:', {
        PostType,
        Name,
        Email,
        Auth0UserId,
        Caption,
      });

      // Step 1: Create the post
      this.createPost(PostType, Name, Email, Auth0UserId, Caption).subscribe({
        next: (postResponse) => {
          const postId = postResponse.postId; // Assuming the backend returns the created post's ID

          // Reward daily coins after post creation
          this.rewardDailyCoins(Auth0UserId).subscribe({
            next: (rewardResponse) => {
              console.log('Daily coins rewarded:', rewardResponse);

              if (MediaFile) {
                // Step 2: If media is present, upload it
                this.uploadMedia(MediaFile, Auth0UserId).subscribe({
                  next: (mediaResponse) => {
                    const mediaLink = mediaResponse.url; // Assuming backend returns the media link
                    // Step 3: Update the post with the media link
                    this.updatePostWithMedia(postId, mediaLink).subscribe({
                      next: (updateResponse) => {
                        console.log(updateResponse);
                        observer.next(rewardResponse); // Final success response
                        observer.complete();
                      },
                      error: (error) => {
                        observer.error(
                          `Error updating post with media: ${error}`
                        );
                      },
                    });
                  },
                  error: (error) => {
                    observer.error(`Error uploading media: ${error}`);
                  },
                });
              } else {
                // If no media, return the created post response
                observer.next(rewardResponse);
                observer.complete();
              }
            },
            error: (error) => {
              console.error('Error rewarding daily coins:', error); // Inspect the full error
              observer.error(
                `Error rewarding daily coins: ${JSON.stringify(error)}`
              ); // Use JSON.stringify for clarity;
            },
          });
        },
        error: (error) => {
          observer.error(`Error creating post: ${error}`);
        },
      });
    });
  }

  // Function to create a new post
  addComment(
    postId: string,
    auth0UserId: string,
    text: string,
    commenterEmail: string,
    postEmail: string,
    name: string,
    commentId?: string
  ): Observable<any> {
    const body = {
      postId,
      auth0UserId,
      text,
      commenterEmail,
      postEmail,
      name,
      commentId: commentId || '',
    };
    console.log('Request body:', body);
    return this.http.post(`${this.baseUrl}posts/comments`, body, {
      headers: new HttpHeaders({ 'Content-Type': 'application/json' }),
    });
  }

  //Function for likes
  toggleLike(
    postId: string,
    auth0UserId: string,
    commentId?: string,
    replyId?: string
  ): Observable<any> {
    const body = {
      postId,
      auth0UserId,
      commentId: commentId || '',
      replyId: replyId || '',
    };
    console.log('Request body:', body);
    return this.http.put(`${this.baseUrl}posts/like`, body, {
      headers: new HttpHeaders({ 'Content-Type': 'application/json' }),
    });
  }

  //Delete Post
  deletePost(postId: string, auth0UserId: string): Observable<any> {
    const options = {
      headers: new HttpHeaders({ 'Content-Type': 'application/json' }),
      body: {
        auth0UserId,
      },
    };

    console.log('Request options:', options);

    return this.http.delete(`${this.baseUrl}posts/${postId}`, options);
  }

  //Function for editing comments
  editComment(
    text: string,
    postId: string,
    auth0UserId: string,
    commentId: string,
    replyId?: string
  ): Observable<any> {
    const body = {
      text,
      postId,
      auth0UserId,
      commentId: commentId,
      replyId: replyId || undefined,
    };
    console.log('Request body:', body);
    return this.http.put(`${this.baseUrl}posts/comments`, body, {
      headers: new HttpHeaders({ 'Content-Type': 'application/json' }),
    });
  }

  //Function for deleting comments
  deleteComment(
    postId: string,
    auth0UserId: string,
    commentId: string,
    replyId?: string
  ): Observable<any> {
    const options = {
      headers: new HttpHeaders({ 'Content-Type': 'application/json' }),
      body: {
        auth0UserId,
        postId,
        commentId,
        replyId: replyId || undefined,
      },
    };

    console.log('Request options:', options);
    return this.http.delete(`${this.baseUrl}posts/comments`, options);
  }
}
